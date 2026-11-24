// Port TypeScript du moteur de règles et du vérificateur C#, utilisé quand l'API n'est pas joignable
// (démo GitHub Pages). L'application réelle appelle l'API .NET Core et le LLM local.
import { Analysis, ExtractedFact } from './models';

export function normalize(t: string): string {
  return (t ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
    .replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

export function verify(a: Omit<Analysis, 'faitsVerifies' | 'tauxAncrage' | 'statut'>, text: string): Analysis {
  const src = normalize(text);
  const faits = a.faits.map(f => ({ ...f, verifie: normalize(f.citation).length >= 3 && src.includes(normalize(f.citation)) }));
  const ok = faits.filter(f => f.verifie).length;
  return {
    ...a, faits, faitsVerifies: ok,
    tauxAncrage: faits.length ? Math.round((1000 * ok) / faits.length) / 10 : 0,
    statut: faits.length > 0 && ok === faits.length ? 'Prêt pour le gestionnaire' : 'À relire',
  };
}

const has = (t: string, ...w: string[]) => w.some(x => t.includes(x));
const MONTHS = 'janvier|février|fevrier|mars|avril|mai|juin|juillet|août|aout|septembre|octobre|novembre|décembre|decembre';

export function analyzeLocally(text: string): Analysis {
  const t = text.toLowerCase();
  const type = has(t, 'voiture', 'véhicule', 'vehicule', 'accrochage', 'collision', 'pare-chocs', 'conducteur') ? 'Automobile'
    : has(t, 'dégât des eaux', 'degat des eaux', 'fuite', 'incendie', 'cambriolage', 'logement', 'appartement') ? 'Habitation'
    : has(t, 'hôpital', 'hopital', 'médecin', 'medecin', 'blessure') ? 'Santé' : 'Autre';
  const blesse = has(t, 'blessé', 'blesse', 'urgences', 'hôpital', 'hopital');
  const urgence = blesse || has(t, 'incendie', 'inhabitable') ? 'Haute' : has(t, 'fuite', 'bloqué', 'bloque') ? 'Moyenne' : 'Basse';
  const faits: ExtractedFact[] = [];
  const add = (champ: string, re: RegExp) => { const m = text.match(re); if (m) faits.push({ champ, valeur: m[0].trim(), citation: m[0].trim(), verifie: false }); };
  add('Date', new RegExp(`\\b(le|du)\\s+\\d{1,2}\\s+(${MONTHS})(\\s+\\d{4})?`, 'i'));
  add('Lieu', /(?<=^|\s)(à|sur|rue|avenue|boulevard)\s+[A-ZÉÈ][\wÀ-ÿ'\- ]{2,40}/);
  add('Police', /(contrat|police)\s*(n°|no|numéro)?\s*[A-Z0-9-]{5,}/i);
  const pieces: string[] = [];
  if (type === 'Automobile' && !has(t, 'constat')) pieces.push('Constat amiable signé');
  if (!has(t, 'photo')) pieces.push('Photos des dommages');
  if (type === 'Habitation' && !has(t, 'facture', 'devis')) pieces.push('Devis ou factures de réparation');
  if (blesse && !has(t, 'certificat')) pieces.push('Certificat médical');
  return verify({ typeSinistre: type, urgence, resume: `Sinistre ${type.toLowerCase()} déclaré, urgence ${urgence.toLowerCase()}.`,
    faits, piecesManquantes: pieces, moteur: 'Démo navigateur (règles)' }, text);
}

export const SAMPLES = [
  { label: 'Accident auto', text: "Bonjour, le 12 novembre 2026 vers 8h, ma voiture a été percutée à l'arrière sur l'avenue de la Liberté à Luxembourg. Le conducteur adverse a reconnu sa faute, nous avons rempli un constat. Pare-chocs arrière enfoncé, feu cassé. Contrat n° AUTO-458812." },
  { label: 'Dégât des eaux', text: "Fuite d'eau importante depuis l'appartement du dessus le 20 novembre 2026, rue Philippe II à Luxembourg. Le plafond de la cuisine est taché et le parquet gonflé. Je joins des photos. Police n° HAB-990177." },
  { label: 'Incendie avec blessé', text: "Incendie dans la cuisine du logement le 2 mars 2026 à Esch-sur-Alzette, la pièce est inhabitable. Mon fils a été légèrement blessé à la main et conduit aux urgences. Contrat n° HAB-301245." },
];

// Réponse réelle d'un LLM enregistrée pour la démo : il a ajouté un blessé qui n'existe pas dans la déclaration.
export const RECORDED_LLM = {
  label: 'LLM qui invente',
  text: "Le 18 novembre 2026, en sortant du parking rue de Hollerich à Luxembourg, j'ai heurté un poteau. Le rétroviseur droit et la portière avant sont rayés. Pas de tiers impliqué. Contrat n° AUTO-772041.",
  raw: {
    typeSinistre: 'Automobile', urgence: 'Haute',
    resume: "Collision avec un poteau en sortie de parking, dommages sur le côté droit. Un passager aurait été blessé.",
    faits: [
      { champ: 'Date', valeur: '18/11/2026', citation: 'Le 18 novembre 2026', verifie: false },
      { champ: 'Lieu', valeur: 'Luxembourg, rue de Hollerich', citation: 'rue de Hollerich à Luxembourg', verifie: false },
      { champ: 'Dommages', valeur: 'Rétroviseur et portière', citation: 'Le rétroviseur droit et la portière avant sont rayés', verifie: false },
      { champ: 'Blessés', valeur: '1 passager', citation: 'mon passager a été blessé au bras', verifie: false },
      { champ: 'Police', valeur: 'AUTO-772041', citation: 'Contrat n° AUTO-772041', verifie: false },
    ],
    piecesManquantes: ['Photos des dommages', 'Certificat médical'],
    moteur: 'LLM local qwen2.5:3b (réponse enregistrée)',
  },
};

export function customerMessage(a: Analysis): string {
  const lines = a.piecesManquantes.map(p => `  • ${p}`).join('\n');
  return a.piecesManquantes.length
    ? `Bonjour,\n\nNous avons bien reçu votre déclaration de sinistre (${a.typeSinistre.toLowerCase()}). Pour traiter votre dossier rapidement, merci de nous transmettre :\n${lines}\n\nVotre gestionnaire reste à votre disposition.\nCordialement`
    : `Bonjour,\n\nNous avons bien reçu votre déclaration de sinistre (${a.typeSinistre.toLowerCase()}). Votre dossier est complet et a été transmis à votre gestionnaire.\nCordialement`;
}
