import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Analysis, ClaimRow } from './models';
import { RECORDED_LLM, SAMPLES, analyzeLocally, customerMessage, verify } from './local-engine';

const API = 'http://localhost:5080';

@Component({
  selector: 'app-root',
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
  <header class="hero">
    <div class="hero-in">
      <div class="brand-row">
        <div class="brand"><span class="logo">◎</span> ClaimLens</div>
        <span class="mode" [class.live]="apiOnline()">
          <i></i>{{ apiOnline() ? 'API .NET Core · ' + model() : 'Démo navigateur' }}
        </span>
      </div>
      <h1>Des déclarations de sinistre analysées par un LLM local,<br><em>et chaque fait prouvé par le texte.</em></h1>
      <div class="stats">
        <div><b>{{ history().length }}</b><span>dossiers analysés</span></div>
        <div><b>{{ avgAnchor() | number:'1.0-0' }} %</b><span>ancrage moyen</span></div>
        <div><b>{{ toReview() }}</b><span>à relire</span></div>
      </div>
    </div>
  </header>

  <ol class="pipeline">
    @for (s of steps; track s; let i = $index) {
      <li [class.on]="stage() > i"><span>{{ i + 1 }}</span>{{ s }}</li>
    }
  </ol>

  <main class="grid">
    <section class="card input">
      <h2>Déclaration de l'assuré</h2>
      <div class="samples">
        @for (s of samples; track s.label) {
          <button class="chip" (click)="pick(s.text, false)">{{ s.label }}</button>
        }
        <button class="chip warn" (click)="pick(recorded.text, true)" title="Réponse réelle d'un LLM qui a inventé un fait">
          <i></i>{{ recorded.label }}
        </button>
      </div>
      <textarea [ngModel]="text()" (ngModelChange)="text.set($event); useRecorded.set(false)" rows="8"
        placeholder="Collez ici la déclaration de sinistre…"></textarea>
      <button class="primary" (click)="analyze()" [disabled]="busy() || !text().trim()">
        @if (busy()) { <span class="spin"></span> Analyse en cours… } @else { Analyser la déclaration → }
      </button>
      @if (error()) { <p class="error">{{ error() }}</p> }

      @if (result()) {
        <h3>Preuves dans le texte</h3>
        <p class="evidence">
          @for (seg of segments(); track $index) {
            @if (seg.hit) { <mark>{{ seg.t }}</mark> } @else { <span>{{ seg.t }}</span> }
          }
        </p>
      }
    </section>

    <section class="card output" [attr.data-u]="result()?.urgence">
      @if (result(); as r) {
        <div class="kpis">
          <div class="kpi"><span>Type de sinistre</span><b>{{ r.typeSinistre || '?' }}</b></div>
          <div class="kpi"><span>Urgence</span><b class="pill" [attr.data-u]="r.urgence">{{ r.urgence || '?' }}</b></div>
          <div class="kpi gauge">
            <div class="ring" [style.--p]="r.tauxAncrage" [class.low]="r.tauxAncrage < 100"><b>{{ r.tauxAncrage | number:'1.0-0' }}%</b></div>
            <span>faits ancrés</span>
          </div>
        </div>

        <div class="status" [class.ok]="ready()">
          <b>{{ ready() ? '✓' : '⚠' }} {{ r.statut }}</b>
          <small>{{ r.faitsVerifies }} / {{ r.faits.length }} faits retrouvés mot pour mot · {{ r.moteur }}</small>
        </div>
        @for (f of blocked(); track $index) {
          <div class="blocked"><b>Fait bloqué · {{ f.champ }}</b> « {{ f.citation }} » est introuvable dans la déclaration :
            le LLM l'a inventé, il ne sera pas transmis.</div>
        }
        <p class="resume">{{ r.resume }}</p>

        <h3>Faits extraits</h3>
        <table>
          <thead><tr><th></th><th>Champ</th><th>Valeur</th><th>Citation</th></tr></thead>
          <tbody>
            @for (f of r.faits; track $index) {
              <tr [class.bad]="!f.verifie">
                <td class="state">{{ f.verifie ? '✓' : '✗' }}</td>
                <td><b>{{ f.champ }}</b></td><td>{{ f.valeur }}</td><td class="quote">« {{ f.citation }} »</td>
              </tr>
            } @empty { <tr><td colspan="4" class="muted">Aucun fait extrait.</td></tr> }
          </tbody>
        </table>

        <div class="two">
          <div>
            <h3>Pièces à demander</h3>
            <ul class="todo">
              @for (p of r.piecesManquantes; track p) { <li>{{ p }}</li> }
              @empty { <li class="done">Dossier complet</li> }
            </ul>
          </div>
          <div class="msg">
            <h3>Message à l'assuré <button class="copy" (click)="copy()">{{ copied() ? 'Copié ✓' : 'Copier' }}</button></h3>
            <pre>{{ message() }}</pre>
          </div>
        </div>
      } @else {
        <div class="empty">
          <div class="big">◎</div>
          <p><b>Choisissez un exemple</b> ou collez une déclaration, puis lancez l'analyse.</p>
          <p class="muted">Essayez « LLM qui invente » : le blessé ajouté par le modèle est bloqué et le dossier part en relecture.</p>
        </div>
      }
    </section>
  </main>

  @if (history().length) {
    <section class="card history">
      <h2>Historique {{ apiOnline() ? '(base SQL)' : '(session de démo)' }}</h2>
      <table>
        <thead><tr><th>#</th><th>Date</th><th>Type</th><th>Urgence</th><th>Statut</th><th>Ancrage</th></tr></thead>
        <tbody>
          @for (h of history(); track h.id) {
            <tr><td class="muted">{{ h.id }}</td><td>{{ h.creeLe | date:'dd/MM HH:mm' }}</td><td>{{ h.typeSinistre }}</td>
              <td><span class="pill" [attr.data-u]="h.urgence">{{ h.urgence }}</span></td>
              <td><span class="tag" [class.ok]="h.statut.startsWith('Prêt')">{{ h.statut }}</span></td>
              <td><div class="bar"><i [style.width.%]="h.tauxAncrage"></i></div>{{ h.tauxAncrage | number:'1.0-0' }} %</td></tr>
          }
        </tbody>
      </table>
    </section>
  }
  <footer>.NET Core 8 · Angular 19 · Ollama (LLM local) · EF Core · SQLite ou SQL Server · OpenAPI · xUnit</footer>
  `,
  styleUrl: './app.component.css',
})
export class AppComponent implements OnInit {
  private http = inject(HttpClient);
  samples = SAMPLES;
  recorded = RECORDED_LLM;
  steps = ['Déclaration', 'LLM local', 'Vérification des citations', 'Gestionnaire'];
  text = signal(SAMPLES[0].text);
  useRecorded = signal(false);
  result = signal<Analysis | null>(null);
  analyzedText = signal('');
  busy = signal(false);
  error = signal('');
  copied = signal(false);
  apiOnline = signal(false);
  model = signal('');
  history = signal<ClaimRow[]>([]);
  stage = signal(1);

  ready = computed(() => this.result()?.statut.startsWith('Prêt') ?? false);
  blocked = computed(() => this.result()?.faits.filter(f => !f.verifie) ?? []);
  message = computed(() => (this.result() ? customerMessage(this.result()!) : ''));
  avgAnchor = computed(() => {
    const h = this.history();
    return h.length ? h.reduce((s, x) => s + x.tauxAncrage, 0) / h.length : 0;
  });
  toReview = computed(() => this.history().filter(h => !h.statut.startsWith('Prêt')).length);

  segments = computed(() => {
    const text = this.analyzedText();
    const r = this.result();
    if (!r) return [{ t: text, hit: false }];
    const lower = text.toLowerCase();
    const ranges = r.faits.filter(f => f.verifie)
      .map(f => { const i = lower.indexOf(f.citation.toLowerCase()); return [i, i + f.citation.length] as [number, number]; })
      .filter(([i]) => i >= 0).sort((a, b) => a[0] - b[0]);
    const out: { t: string; hit: boolean }[] = [];
    let pos = 0;
    for (const [s, e] of ranges) {
      if (s < pos) continue;
      if (s > pos) out.push({ t: text.slice(pos, s), hit: false });
      out.push({ t: text.slice(s, e), hit: true });
      pos = e;
    }
    if (pos < text.length) out.push({ t: text.slice(pos), hit: false });
    return out;
  });

  async ngOnInit() {
    try {
      const h = await firstValueFrom(this.http.get<{ model: string }>(`${API}/api/health`));
      this.apiOnline.set(true);
      this.model.set(h.model);
      await this.loadHistory();
    } catch {
      this.apiOnline.set(false);
    }
  }

  pick(text: string, recorded: boolean) {
    this.text.set(text);
    this.useRecorded.set(recorded);
  }

  async analyze() {
    const text = this.text();
    this.busy.set(true);
    this.error.set('');
    this.copied.set(false);
    this.stage.set(2);
    try {
      let r: Analysis;
      if (this.useRecorded()) {
        await new Promise(res => setTimeout(res, 450));
        r = verify(RECORDED_LLM.raw, text);
      } else if (this.apiOnline()) {
        r = (await firstValueFrom(this.http.post<{ analysis: Analysis }>(`${API}/api/claims`, { declaration: text }))).analysis;
      } else {
        await new Promise(res => setTimeout(res, 350));
        r = analyzeLocally(text);
      }
      this.result.set(r);
      this.analyzedText.set(text);
      this.stage.set(r.statut.startsWith('Prêt') ? 4 : 3);
      if (this.apiOnline() && !this.useRecorded()) await this.loadHistory();
      else this.history.update(h => [{ id: h.length + 1, creeLe: new Date().toISOString(), typeSinistre: r.typeSinistre,
        urgence: r.urgence, statut: r.statut, tauxAncrage: r.tauxAncrage }, ...h]);
    } catch {
      this.error.set("L'analyse a échoué. Vérifiez que l'API est lancée.");
      this.stage.set(1);
    } finally {
      this.busy.set(false);
    }
  }

  async copy() {
    try { await navigator.clipboard.writeText(this.message()); this.copied.set(true); } catch { /* presse-papiers indisponible */ }
  }

  private async loadHistory() {
    this.history.set(await firstValueFrom(this.http.get<ClaimRow[]>(`${API}/api/claims`)));
  }
}
