export interface ExtractedFact { champ: string; valeur: string; citation: string; verifie: boolean; }
export interface Analysis {
  typeSinistre: string; urgence: string; resume: string;
  faits: ExtractedFact[]; piecesManquantes: string[]; moteur: string;
  faitsVerifies: number; tauxAncrage: number; statut: string;
}
export interface ClaimRow { id: number; creeLe: string; typeSinistre: string; urgence: string; statut: string; tauxAncrage: number; }
