// J.A.R.V.I.S. HUD nativo POR SESSAO (WinForms + GDI+, ultra-leve ~25MB).
// Uso: jarvis-hud-wf.exe <session-id>   (uma janela por sessao, mutex dedupe)
//      jarvis-hud-wf.exe --shot <out.png> [fable]        (1 frame sintetico p/ QA visual)
//      jarvis-hud-wf.exe --shot-boot <out.png> <p> [fable]  (frame da IGNICAO em p=0..1)
//      jarvis-hud-wf.exe --shot-shut <out.png> <p>          (frame do desligamento em p=0..1)
// Le hud-sessions\<sid>\ (meta.txt + feed.txt + progress.txt) escrito pelos hooks/daemon.
// Mostra: nucleo do reator animado (aneis + raios + motas de energia), status em capsula
// com LED respirando, sessao + etiqueta do MODELO real, TEMPO DE OP., barra ATIVIDADE/
// CARGA com pico VU, ACOES, APM + tendencia, HA Xs (frescor), sparkline de PULSO e um
// FEED de telemetria (chips coloridos; a fala do Jarvis pisca; linha nova ganha accent).
// CINEMATICAS one-shot: IGNICAO na abertura (fisga de luz -> linha CRT -> abre -> aquece
// + flare), desligamento (esfria com glitch CRT -> colapso -> ponto) e transicao de
// modelo (onda de choque + tranco fisico). Acabamento: vidro com scanlines + vinheta,
// grade blueprint e cantoneiras de mira. Sem relogio de parede.
//
// PERFORMANCE (PC fraco): animacao continua repinta SO a area do nucleo
// (Invalidate(atomRect)) a ~15fps (30 no Fable). TODOS os sinais novos (APM, sparkline,
// HA Xs, carga, pico VU, flash, accent) sao calculados 1x/segundo no DataTick e
// desenhados no Invalidate() cheio de 1Hz. A decoracao nova (grade, capsula, vidro) vive
// nesse mesmo passo de 1Hz — nada novo entra no loop de 66ms alem de 5 primitivas do
// nucleo (anel tracejado + 2 motas). As cinematicas (ignicao/desligamento/transicao)
// rodam a ~120fps por ~1.5s e DEVOLVEM o intervalo normal ao terminar (one-shot).
// O vidro (scanlines+vinheta) e 1 bitmap gerado uma unica vez e cacheado (classe Cine).
// Fonte ASCII de proposito (acentos vem dos arquivos UTF8 em runtime).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

class JarvisHudWF : Form {
  string sid, dir, feedPath, metaPath, hbPath, endPath, donePath, modelPath;
  bool fable;                     // sessao rodando o FABLE 5 (classe Mythos) -> visual especial
  // transicao CINEMATICA entre modos (troca de modelo ao vivo): heat 0=normal .. 1=FABLE
  double heat = 0;
  bool inTrans = false; long transStart = 0; double heatFrom = 0, heatTo = 0;
  const long TRANS_MS = 1600;
  string title = "";
  long startTs = 0;
  double phase = 0;
  long lastFeedTs = 0, endShownAt = 0;
  long feedLen = 0, actions = 0;
  int taskDone = 0, taskTotal = 0;
  long bornMs = NowMs();
  const long IDLE_CLOSE = 600000; // 10 min sem atividade -> fecha sozinho (fallback)
  const long DONE_CLOSE = 20000;  // 20s depois que o Claude termina a resposta (hook Stop escreve "done") -> fecha
  List<string[]> feed = new List<string[]>();  // {ts, tag, text}
  const int FEEDN = 8;
  bool dragging; Point dragStart;
  int pid; bool userMoved, movedDuringDrag;   // arrasto manual tira a janela do auto-layout
  WinTimer dataTimer, animTimer, layoutTimer;
  CultureInfo inv = CultureInfo.InvariantCulture;
  Mutex mutex;
  // ---- sequencia de desligamento (esfriar o nucleo + colapso estilo CRT) ----
  bool closing = false; long closeStartMs = 0; Bitmap shotBmp = null, coldBmp = null;
  const long SHUT_MS = 1500;   // duracao da animacao de desligamento
  // ---- sequencia de IGNICAO (boot: fisga de luz -> linha CRT -> abre -> aquece) ----
  bool booting = false; long bootStartMs = 0; Bitmap bootBmp = null;
  const long BOOT_MS = 1400;   // duracao da ignicao (one-shot, espelho do desligamento)
  // ---- FUNDO CACHEADO: o painel inteiro MENOS o nucleo e desenhado num bitmap e refeito
  // so quando os dados mudam (1x/s). Cada quadro do nucleo apenas blita a regiao do atomo
  // desse cache e desenha o nucleo vivo por cima -> quadro barato -> da pra rodar liso a
  // 30/60fps sem repetir o Render() pesado (grade, cantoneiras, vidro, MeasureString...).
  Bitmap bgBmp = null; bool bgDirty = true; bool periodSet = false;
  // ---- FX cinematicos por EVENTO: ondas/explosoes/flashes breves de painel inteiro.
  // Enquanto ha FX vivo (ou 1 quadro depois de acabar) o anim tick pinta o painel TODO;
  // senao so o nucleo -> o custo extra so acontece nos instantes de evento (barato na media). ----
  struct Fx { public int type; public long start; public int dur; }
  List<Fx> fxs = new List<Fx>();
  const int FX_RIPPLE = 0, FX_BURST = 1, FX_FLASH = 2;
  long fxLastFeed = 0; int fxLastDone = -1; string fxLastStatus = ""; bool wasFx = false;
  // ---- MINIMIZAR: a telinha colapsa numa MINI-CAPSULA reator no canto e reabre no clique.
  // Mesmo processo, dois estados (cheio / mini); o morph faz a implosao/expansao cinematica.
  bool minimized = false, morphing = false; long morphStart = 0; int morphDir = 0;
  const int MORPH_MS = 300, MINI_W = 182, MINI_H = 54;
  Rectangle morphStartRect, morphDestRect;   // sanfona: a janela voa de start->dest (reservando a pegada cheia)
  Bitmap fullShot = null, miniShotBmp = null, miniBg = null; bool miniBgDirty = true;
  static Rectangle minRect = new Rectangle(W - 45, 8, 16, 18);              // botao minimizar (cheio)
  static Rectangle miniCloseRect = new Rectangle(MINI_W - 16, 5, 11, 11);   // fechar (mini)
  protected override bool ShowWithoutActivation { get { return true; } }   // reaparecer do "esconder todas" nao rouba o foco
  // SUCÇÃO (v1.6.1): ao esconder, a janela VOA ate o botao desvanecendo; ao mostrar, renasce
  // no botao e voa de volta. 0=normal · 1=voando pro botao · 2=escondida · 3=voando de volta.
  int hideFly = 0; long hideFlyT0 = 0; Point hideFlyFrom;
  Point BtnPoint() {
    Rectangle b;
    if (HudLayout.ReadBtnPos(out b)) return new Point(b.X + b.Width - Width, b.Y);   // canto sup-dir da janela encosta no botao
    var wa = Screen.PrimaryScreen.WorkingArea; return new Point(wa.Right - Width - 12, wa.Top + 42);
  }
  [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint p);
  [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint p);

  // ---- vitalidade (derivada 1x/s do que ja esta em memoria) ----
  long[] actTs = new long[300];   // ring de timestamps de acao (p/ APM, carga, sparkline)
  int apm = 0, apmPeak = 0, apmRef = 0;
  int[] spark = new int[16];      // 16 baldes de ~4s = ~64s
  int[] apmHist = new int[10]; int apmHistIdx = 0;   // ring 1x/s -> tendencia vs ~10s atras
  double loadPct = 0;             // "carga do reator" = acoes nos ultimos 30s / TETO
  double loadPeak = 0;            // pico VU da carga: segura o maximo e decai (realismo de medidor)
  long lastJvsTs = 0;             // ultima fala do Jarvis (p/ flash do chip)
  string modelShort = "";         // etiqueta do modelo real da sessao (OPUS/SONNET/...)
  // ---- reator REATIVO: gira/brilha conforme a atividade real (carga + APM) ----
  double spin = 0; long spinMs = 0;   // angulo acumulado (velocidade variavel sem "saltos")
  double CoreEnergy() { return Math.Max(loadPct, Math.Min(1.0, apm / 45.0)); }   // 0..1 atividade

  static Color Ink1 = C("#121F17"), Ink2 = C("#070E09");
  static Color Amber = C("#E8B24A"), AmberBright = C("#F4C25C"), AmberMut = C("#BE9E6C"), AmberDeep = C("#8A6A2E");
  static Color TextC = C("#DCCDAB"), Online = C("#86E3A6"), BorderC = C("#C9A877"), Faint = C("#6C786E"), Red = C("#E8794C");
  static Color MythGold = C("#FFD98A"), MythPale = C("#FFF4DC");   // FABLE 5: ouro-branco classe Mythos
  static Color Ember = C("#FF7A3C"), EmberDeep = C("#B5471F");     // FABLE 5: brasa do overheat (forca total)
  static Color InkF1 = C("#26150A"), InkF2 = C("#0F0603");         // FABLE 5: fundo aquecido pelo nucleo
  static Color C(string h) { return ColorTranslator.FromHtml(h); }

  Font fTitle = new Font("Consolas", 12f, FontStyle.Bold);
  Font fStat = new Font("Consolas", 7.5f, FontStyle.Bold);
  Font fSess = new Font("Consolas", 8.5f, FontStyle.Regular);
  Font fLbl = new Font("Consolas", 7f, FontStyle.Regular);
  Font fBig = new Font("Consolas", 17f, FontStyle.Bold);
  Font fBig2 = new Font("Consolas", 15f, FontStyle.Bold);
  Font fTiny = new Font("Consolas", 6.75f, FontStyle.Regular);
  Font fChip = new Font("Consolas", 6.75f, FontStyle.Bold);
  Font fFeed = new Font("Segoe UI", 9f, FontStyle.Regular);
  Font fClose = new Font("Consolas", 11f, FontStyle.Bold);

  const int W = 380, H = 300, PAD = 16, RAD = 16;
  static Rectangle closeRect = new Rectangle(W - 24, 8, 16, 18);   // fechar (canto sup-direito)
  Rectangle atomRect = new Rectangle(PAD - 4, 48, 80, 84);

  // ---- layout das metricas / sparkline ----
  const int MX = 100;    // coluna esquerda (TEMPO, ACOES)
  const int X2 = 210;    // coluna direita (ATIVIDADE/CARGA, APM)
  const int BARX = 210, BARW = 112, BARY = 64, BARH = 9;
  const int SPKX = 100, SPKW = 256, SPKBASE = 129, SPKTOP = 115;
  const int DIVY = 133;

  [STAThread]
  static void Main(string[] args) {
    if (args.Length >= 1 && args[0] == "--layout-test") {   // auto-teste do dock: 0 = zero sobreposicao
      var sb = new StringBuilder();
      int fails = HudLayout.SelfTest(sb);
      string outp = args.Length >= 2 ? args[1] : Path.Combine(Path.GetTempPath(), "jarvis-layout-test.txt");
      try { File.WriteAllText(outp, sb.ToString()); } catch {}
      Environment.Exit(fails); return;
    }
    if (args.Length >= 2 && args[0] == "--shot") { Shot(args[1], args.Length > 2 && args[2] == "fable"); return; }
    if (args.Length >= 2 && args[0] == "--shot-mini") { ShotMini(args[1], args.Length > 2 && args[2] == "fable"); return; }
    if (args.Length >= 3 && args[0] == "--shot-shut") { ShotShut(args[1], double.Parse(args[2], CultureInfo.InvariantCulture)); return; }
    if (args.Length >= 3 && args[0] == "--shot-boot") { ShotBoot(args[1], double.Parse(args[2], CultureInfo.InvariantCulture), args.Length > 3 && args[3] == "fable"); return; }
    if (args.Length >= 2 && args[0] == "--fanout-shot") {   // [done] [mini] [qa|qa_ultra] em qualquer ordem
      bool fdone = false, fmini = false; string fkind = "";
      for (int i = 2; i < args.Length; i++) { if (args[i] == "done") fdone = true; else if (args[i] == "mini") fmini = true; else if (args[i] == "qa" || args[i] == "qa_ultra") fkind = args[i]; }
      FanoutHud.Shot(args[1], fdone, fkind, fmini); return;
    }
    if (args.Length >= 2 && args[0] == "--fanout") { FanoutHud.Run(args[1]); return; }   // telinha de fan-out (substitui o CMD)
    if (args.Length >= 1 && args[0] == "--minall-btn") { Application.EnableVisualStyles(); Application.Run(new MinAllButton()); return; }   // botao flutuante esconder/mostrar todas
    if (args.Length >= 2 && args[0] == "--btn-shot") { MinAllButton.Shot(args[1], args.Length > 2 && args[2] == "hidden"); return; }   // QA do botao (disco | pilula "hidden")
    string sid = args.Length > 0 ? args[0] : "global";
    bool made; var m = new Mutex(true, "JarvisHud_" + Sanitize(sid), out made);
    if (!made) return;                       // ja existe janela p/ essa sessao
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new JarvisHudWF(sid, m));
  }

  // render 1 frame sintetico p/ QA visual (sem janela/loop). Arg "fable" = modo FABLE 5.
  static void Shot(string outPng, bool fableMode) {
    var f = new JarvisHudWF("shot-demo", null);
    f.Seed();
    if (fableMode) {
      f.fable = true; f.heat = 1;
      f.feed.Add(new string[] { NowMs().ToString(), "JVS", "Operando em forca total, senhor." });
    }
    var bmp = new Bitmap(W, H);
    using (var g = Graphics.FromImage(bmp)) f.Render(g);
    bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
  }

  // QA visual da MINI-CAPSULA (frame sintetico). Arg "fable" = modo FABLE 5.
  static void ShotMini(string outPng, bool fableMode) {
    var f = new JarvisHudWF("shot-demo", null);
    f.Seed(); f.title = "GC Haven - Geral";
    if (fableMode) { f.fable = true; f.heat = 1; }
    var bmp = new Bitmap(MINI_W, MINI_H);
    using (var g = Graphics.FromImage(bmp)) f.RenderMini(g);
    bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
  }

  // QA visual: renderiza 1 frame da IGNICAO no progresso p (0..1); arg "fable" = modo FABLE 5
  static void ShotBoot(string outPng, double p, bool fableMode) {
    var f = new JarvisHudWF("shot-demo", null);
    f.Seed();
    if (fableMode) { f.fable = true; f.heat = 1; f.modelShort = ""; }
    f.bootBmp = new Bitmap(W, H);
    using (var g0 = Graphics.FromImage(f.bootBmp)) f.Render(g0);
    f.booting = true; f.bootStartMs = NowMs() - (long)(p * BOOT_MS);
    var bmp = new Bitmap(W, H);
    using (var g = Graphics.FromImage(bmp)) f.PaintBoot(g);
    bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
  }

  // QA visual: renderiza 1 frame da animacao de desligamento no progresso p (0..1)
  static void ShotShut(string outPng, double p) {
    var f = new JarvisHudWF("shot-demo", null);
    f.Seed();
    f.shotBmp = new Bitmap(W, H);
    using (var g0 = Graphics.FromImage(f.shotBmp)) f.Render(g0);
    f.coldBmp = new Bitmap(W, H);
    using (var gc = Graphics.FromImage(f.coldBmp)) using (var ia = new ImageAttributes()) {
      ia.SetColorMatrix(CoolMatrix(1.0));
      gc.DrawImage(f.shotBmp, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel, ia);
    }
    f.closing = true; f.closeStartMs = NowMs() - (long)(p * SHUT_MS);
    var bmp = new Bitmap(W, H);
    using (var g = Graphics.FromImage(bmp)) f.PaintShutdown(g);
    bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
  }

  static string Sanitize(string s) {
    var sb = new StringBuilder(); foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c); return sb.ToString();
  }

  protected override CreateParams CreateParams {
    get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // WS_EX_TOOLWINDOW: fora do Alt+Tab
  }

  JarvisHudWF(string sessionId, Mutex m) {
    sid = sessionId; mutex = m;
    string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    string root = Path.GetFullPath(Path.Combine(exeDir, "..", "hud-sessions"));
    dir = Path.Combine(root, Sanitize(sid));
    try { Directory.CreateDirectory(dir); } catch {}
    feedPath = Path.Combine(dir, "feed.txt");
    metaPath = Path.Combine(dir, "meta.txt");
    hbPath = Path.Combine(dir, "hb");
    endPath = Path.Combine(dir, "end");
    donePath = Path.Combine(dir, "done");
    modelPath = Path.Combine(dir, "model.txt");

    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false; TopMost = true;
    ClientSize = new Size(W, H);
    StartPosition = FormStartPosition.Manual;
    BackColor = Ink2; DoubleBuffered = true;
    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    using (var gp = RoundedPath(0, 0, W, H, RAD)) Region = new Region(gp);

    if (m == null) return;   // modo --shot: nao inicia timers/posicao

    // timer de ALTA RESOLUCAO durante toda a vida da janela: o WinForms.Timer, sem isso,
    // so entrega quadros em multiplos de ~15.6ms (jitter que fazia o nucleo "travar" a
    // 15fps). Com 1ms de resolucao, 16/33ms saem regulares. A janela so abre em tarefa
    // ativa e fecha ~20s depois -> custo de energia so enquanto o Davi ta trabalhando.
    try { timeBeginPeriod(1); periodSet = true; } catch {}

    pid = System.Diagnostics.Process.GetCurrentProcess().Id;
    if (HudLayout.WantStartMinimized()) {                   // config: abrir ja como mini-capsula estacionada no dock (compartilhada com a fan-out)
      minimized = true;
      ClientSize = new Size(MINI_W, MINI_H);
      try { using (var gpm = RoundedPath(0, 0, MINI_W, MINI_H, RegionRad(MINI_H))) Region = new Region(gpm); } catch {}
      Location = HudLayout.Place(pid, bornMs, MINI_W, MINI_H, false, true);
    } else {
      Location = HudLayout.Place(pid, bornMs, W, H, false, false);
    }
    Beat();
    ReadMeta(); ReadFeed(); ReadModel();
    heat = fable ? 1 : 0; inTrans = false;   // a janela ABRE ja no modo certo, sem transicao

    dataTimer = new WinTimer(); dataTimer.Interval = 1000;
    dataTimer.Tick += delegate { DataTick(); };
    dataTimer.Start();

    layoutTimer = new WinTimer(); layoutTimer.Interval = 60;   // recompacta o dock ~16x/s: a vizinha reencaixa em ate ~60ms ao expandir/minimizar/fechar (transiente sub-perceptivel; o throttle de escrita mantem o disco leve)
    layoutTimer.Tick += delegate { PlaceTick(); };
    layoutTimer.Start();

    animTimer = new WinTimer(); animTimer.Interval = 33;   // nucleo: 30fps ocioso / 60fps operando (DataTick ajusta)
    animTimer.Tick += delegate {
      if (closing) {
        if (NowMs() - closeStartMs >= SHUT_MS) { animTimer.Stop(); RealClose(); return; }
        Invalidate(); return;                       // repinta a janela toda durante o desligamento
      }
      if (booting) {
        if (NowMs() - bootStartMs >= BOOT_MS) { EndBoot(); return; }
        Invalidate(); return;                       // ignicao repinta a janela toda (one-shot)
      }
      if (morphing) {                                 // colapso/expansao da mini-capsula (one-shot)
        double mt = (NowMs() - morphStart) / (double)MORPH_MS;
        if (mt >= 1) { EndMorph(); return; }
        ApplyMorphBounds(mt); Invalidate(); return;
      }
      if (hideFly == 1 || hideFly == 3) {             // SUCÇÃO pro botao / retorno (one-shot ~280ms a ~120fps)
        double ht = (NowMs() - hideFlyT0) / 280.0; if (ht > 1) ht = 1;
        // succao ACELERA pro botao (easeIn); retorno DESACELERA ao pousar (easeOut)
        double hs = hideFly == 1 ? ht * ht : 1 - (1 - ht) * (1 - ht);
        Point A = hideFly == 1 ? hideFlyFrom : BtnPoint();
        Point B = hideFly == 1 ? BtnPoint() : hideFlyFrom;
        Location = new Point((int)Math.Round(A.X + (B.X - A.X) * hs), (int)Math.Round(A.Y + (B.Y - A.Y) * hs));
        try { Opacity = hideFly == 1 ? 1 - hs : hs; } catch { /* ok */ }
        if (ht >= 1) {
          try { animTimer.Interval = 33; } catch { /* ok */ }   // devolve o passo normal (o DataTick reajusta se preciso)
          if (hideFly == 1) { Hide(); try { Opacity = 1; } catch { /* ok */ } Location = hideFlyFrom; hideFly = 2; }
          else { try { Opacity = 1; } catch { /* ok */ } hideFly = 0; }
        }
        return;
      }
      // fase pelo RELOGIO (nao por incremento): velocidade estavel em qualquer fps,
      // sem "saltos" se um tick atrasar -- os satelites do Fable denunciavam (07/07)
      phase = (NowMs() - bornMs) / 500.0;
      UpdateSpin();                                    // rotacao do reator acelera com a atividade
      if (minimized) { Invalidate(); return; }         // mini e pequeno: repinta tudo (barato)
      if (inTrans) {                                   // transicao de modo: a janela INTEIRA anima
        double t = (NowMs() - transStart) / (double)TRANS_MS;
        if (t >= 1) { heat = heatTo; inTrans = false; bgDirty = true; animTimer.Interval = CoreInterval(); }
        else heat = heatFrom + (heatTo - heatFrom) * SmoothStep(t);
        Invalidate(); return;
      }
      // FX vivo (ou o 1o quadro apos acabar, p/ limpar) -> pinta o painel TODO; senao so o nucleo
      bool nowFx = FxActive();
      if (nowFx || wasFx) Invalidate(); else Invalidate(atomRect);
      wasFx = nowFx;
    };
    animTimer.Start();    // nucleo anima SEMPRE (repinta so o atomRect -> custo baixo)

    UpdateStatus();
    if (!minimized) BeginBoot();   // ignicao cinematica so no modo cheio; minimizada abre direto como capsula
    Invalidate();
  }


  // IGNICAO: espelho do desligamento, em tom QUENTE. Congela o 1o frame vivo e o revela
  // em 4 atos (fisga de luz -> linha CRT -> abertura vertical -> aquecimento + flare).
  // One-shot a ~120fps por BOOT_MS; terminou, o custo volta ao regime normal.
  void BeginBoot() {
    booting = true; bootStartMs = NowMs();
    try { bootBmp = new Bitmap(W, H); using (var g = Graphics.FromImage(bootBmp)) Render(g); } catch { bootBmp = null; }
    if (animTimer != null) animTimer.Interval = 8;   // ignicao ~120fps (timer de alta-res ja segurado na abertura)
  }
  void EndBoot() {
    booting = false;
    try { if (bootBmp != null) { bootBmp.Dispose(); bootBmp = null; } } catch {}
    bgDirty = true;                                  // 1o quadro em regime = fundo fresco
    if (animTimer != null && !closing) animTimer.Interval = CoreInterval();
    Invalidate();
  }

  // ------- estado / status -------
  string status = "ONLINE";
  void UpdateStatus() {                            // so decide o rotulo/cor; o nucleo anima sempre
    long now = NowMs();
    if (File.Exists(endPath)) { status = "ENCERRADO"; if (endShownAt == 0) endShownAt = now; }
    else if (now - lastFeedTs < 8000) status = "OPERANDO";
    else status = "ONLINE";
  }

  // recompacta a posicao no dock (~16x/s pelo layoutTimer, alem do DataTick de 1s): quando uma
  // telinha minimiza/expande/fecha, as vizinhas reencaixam em ate ~60ms -> transiente minimo.
  void PlaceTick() {
    if (closing) return;                                       // shutdown em curso: nada
    // "ESCONDER TODAS" (botao flutuante) com SUCÇÃO: marcador presente -> a janela voa ate o
    // botao desvanecendo e some (slot/heartbeat seguem vivos, posicao real preservada);
    // marcador removido -> renasce no botao e voa de volta pro lugar. Janela arrastada
    // esconde tambem (sem Touch: o slot foi Released e o Touch o recriaria no dock).
    bool esconder = HudLayout.IsHidden();
    if (esconder && hideFly == 0 && Visible && !morphing && !booting) {
      hideFly = 1; hideFlyT0 = NowMs(); hideFlyFrom = Location;
      try { animTimer.Interval = 8; } catch { /* ok */ }        // voo a ~120fps (padrao dos one-shots, como o desligamento)
    }
    if (!esconder && hideFly == 2) {
      hideFly = 3; hideFlyT0 = NowMs();
      try { Opacity = 0; } catch { /* ok */ }
      try { animTimer.Interval = 8; } catch { /* ok */ }
      Location = BtnPoint(); Show();
    }
    if (hideFly != 0) { if (!userMoved) HudLayout.Touch(pid); return; }   // voo/escondida: o animTimer conduz, dock congelado
    if (!Visible) Show();                                      // ShowWithoutActivation: nao rouba o foco
    if (userMoved) return;                                     // fora do fluxo: nada
    // SO arraste e morph congelam a posicao (o usuario/o morph a controlam) -> ai so renova o hb.
    // Durante o BOOT a janela DEVE reflowar normal: a ignicao so pinta, nao dona a posicao; congela-la
    // deixava uma vizinha expandir POR CIMA dela por ate ~1,4s (regressao pega na re-auditoria).
    if (dragging || morphing) { HudLayout.Touch(pid); return; }
    var np = HudLayout.Place(pid, bornMs, CurW(), CurH(), false, minimized);
    if (np != Location) Location = np;
  }

  void DataTick() {
    HudLayout.EnsureMinAllButton();   // o botao flutuante "minimizar todas" vive enquanto houver telinha
    Beat();
    bool grew = ReadFeed();
    if (grew) ReadMeta();
    ReadProgress();
    ReadModel();          // modo FABLE 5 acende/apaga junto com o modelo da sessao
    Recompute();          // recalcula APM/carga/sparkline 1x/s (barato)
    UpdateStatus();
    // dispara FX ao detectar EVENTOS reais: telemetria nova -> onda; tarefa 100% -> explosao
    // dourada; energizou (ONLINE->OPERANDO) -> flash. Guardas evitam disparo na 1a leitura.
    if (fxLastFeed > 0 && lastFeedTs > fxLastFeed) AddFx(FX_RIPPLE, 850);
    fxLastFeed = lastFeedTs;
    if (taskTotal > 0 && taskDone >= taskTotal && fxLastDone >= 0 && fxLastDone < taskTotal) AddFx(FX_BURST, 1500);
    fxLastDone = taskDone;
    if (status == "OPERANDO" && fxLastStatus.Length > 0 && fxLastStatus != "OPERANDO") AddFx(FX_FLASH, 750);
    fxLastStatus = status;
    bgDirty = true;       // dados mudaram -> refaz o fundo cacheado no proximo quadro (1x/s)
    if (animTimer != null && !closing && !inTrans && !booting && !morphing) {   // fps segue a atividade
      // durante FX (painel inteiro repinta) baixa p/ ~22fps -> o custo de evento nao pesa em PC fraco
      int want = FxActive() ? 45 : CoreInterval();
      if (animTimer.Interval != want) animTimer.Interval = want;
    }
    // encerra sozinho 20s apos o fim da sessao (com animacao de desligamento)
    if (endShownAt > 0 && NowMs() - endShownAt > 20000) { BeginShutdown(); return; }
    // Claude terminou a resposta (hook Stop escreveu "done") -> desliga apos breve carencia.
    // hud-native apaga "done" quando vem novo prompt/acao, entao nao fecha no meio da tarefa.
    if (endShownAt == 0 && File.Exists(donePath)) {
      long dts = 0; try { long.TryParse(File.ReadAllText(donePath).Trim(), out dts); } catch {}
      if (dts > 0 && NowMs() - dts > DONE_CLOSE) { BeginShutdown(); return; }
    }
    // ocioso demais (sessao parada) -> desliga; reabre no proximo evento
    if (!File.Exists(endPath) && NowMs() - Math.Max(lastFeedTs, bornMs) > IDLE_CLOSE) { BeginShutdown(); return; }
    // pasta sumiu (sessao limpa) -> encerra
    if (!Directory.Exists(dir)) { BeginShutdown(); return; }
    PlaceTick();
    if (minimized) miniBgDirty = true;   // atualiza o texto da mini-capsula 1x/s
    Invalidate();
  }

  // janela deslizante 1x/s: APM (60s), carga (30s), sparkline (16 baldes de 4s), tendencia
  void Recompute() {
    long now = NowMs();
    int c60 = 0, c30 = 0;
    for (int b = 0; b < 16; b++) spark[b] = 0;
    for (int i = 0; i < actTs.Length; i++) {
      long t = actTs[i]; if (t <= 0) continue;
      long d = now - t; if (d < 0) continue;
      if (d <= 60000) c60++;
      if (d <= 30000) c30++;
      if (d < 64000) { int idx = (int)(d / 4000); if (idx >= 0 && idx < 16) spark[15 - idx]++; }
    }
    apm = c60; if (apm > apmPeak) apmPeak = apm;
    loadPct = Math.Min(1.0, c30 / 8.0);
    loadPeak = Math.Max(loadPct, Math.Max(0, loadPeak - 0.06));   // pico VU: segura e decai ~6%/s
    apmRef = apmHist[apmHistIdx];               // valor de ~10s atras (mais antigo do ring)
    apmHist[apmHistIdx] = apm; apmHistIdx = (apmHistIdx + 1) % apmHist.Length;
  }

  void Beat() { try { File.WriteAllText(hbPath, NowMs().ToString()); } catch {} }

  void ReadMeta() {
    try {
      if (!File.Exists(metaPath)) return;
      var l = File.ReadAllLines(metaPath);
      if (l.Length >= 1) title = l[0];
      if (l.Length >= 2) { long t; if (long.TryParse(l[1].Trim(), out t)) startTs = t; }
    } catch {}
  }

  void ReadProgress() {
    try {
      string pp = Path.Combine(dir, "progress.txt");
      if (!File.Exists(pp)) return;
      var p = File.ReadAllText(pp).Split('\t');
      if (p.Length >= 2) { int c, t; if (int.TryParse(p[0].Trim(), out c) && int.TryParse(p[1].Trim(), out t)) { taskDone = c; taskTotal = t; } }
    } catch {}
  }

  // modo FABLE 5: statusline/hooks gravam o modelo da sessao em model.txt (linha 1 = id).
  // Fable no nucleo -> reator classe Mythos (ouro-branco, 16 raios, satelites) + badge.
  // Mudou o modelo NO MEIO da sessao? Dispara a transicao cinematica (aquecer/esfriar).
  void ReadModel() {
    try {
      bool nf = false; string id = "";
      if (File.Exists(modelPath)) {
        var l = File.ReadAllLines(modelPath);
        if (l.Length > 0) id = l[0].Trim().ToLowerInvariant();
        nf = id.Contains("fable");
      }
      modelShort = ShortModel(id);
      if (nf != fable) { fable = nf; BeginModelTrans(); }
    } catch {}
  }

  // etiqueta curta do modelo real da sessao (realismo de instrumento; Fable ja tem badge)
  static string ShortModel(string id) {
    if (id.Length == 0 || id.Contains("fable")) return "";
    if (id.Contains("opus")) return "OPUS";
    if (id.Contains("sonnet")) return "SONNET";
    if (id.Contains("haiku")) return "HAIKU";
    var sb = new StringBuilder();
    foreach (char c in id) { if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c)); if (sb.Length >= 8) break; }
    return sb.ToString();
  }

  // transicao entre modos: aquece (normal->Fable) ou esfria (Fable->normal) a janela
  // inteira por TRANS_MS a ~60fps. One-shot: fora da transicao o custo volta ao normal.
  void BeginModelTrans() {
    if (mutex == null) { heat = fable ? 1 : 0; return; }   // modo --shot: sem animacao
    if (minimized || morphing) { heat = fable ? 1 : 0; inTrans = false; miniBgDirty = true; return; }   // mini: troca sem a onda; ZERA inTrans p/ nao ficar preso na pele errada ao restaurar (achado #3)
    inTrans = true; transStart = NowMs(); heatFrom = heat; heatTo = fable ? 1 : 0;
    if (animTimer != null && !closing) animTimer.Interval = 16;
  }

  // le só o que cresceu no feed (append-only) -> baixo custo; conta acoes; mantem ultimas N
  bool ReadFeed() {
    try {
      if (!File.Exists(feedPath)) return false;
      long len = new FileInfo(feedPath).Length;
      if (len == feedLen) return false;
      if (len < feedLen) { feedLen = 0; actions = 0; apmPeak = 0; Array.Clear(actTs, 0, actTs.Length); feed.Clear(); } // truncou -> recomeca
      using (var fs = new FileStream(feedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
        fs.Seek(feedLen, SeekOrigin.Begin);
        int toRead = (int)Math.Min(len - feedLen, 65536);
        var buf = new byte[toRead];
        int got = fs.Read(buf, 0, toRead);
        string chunk = Encoding.UTF8.GetString(buf, 0, got);
        int cut = chunk.LastIndexOf('\n');
        if (cut < 0) return false;                    // ainda sem linha completa
        string complete = chunk.Substring(0, cut + 1);
        feedLen += Encoding.UTF8.GetByteCount(complete);
        foreach (var raw in complete.Split('\n')) {
          if (raw.Length == 0) continue;
          var parts = raw.Split('\t');
          long ts = 0; if (parts.Length > 0) long.TryParse(parts[0], out ts);
          string tag = parts.Length > 1 ? parts[1] : "--";
          string text = parts.Length > 2 ? parts[2] : raw;
          feed.Add(new string[] { ts.ToString(), tag, text });
          lastFeedTs = ts;
          actTs[(int)(actions % actTs.Length)] = ts; actions++;   // alimenta APM/sparkline
          if (tag == "JVS") lastJvsTs = ts;                        // marca a fala p/ flash
        }
        while (feed.Count > FEEDN + 2) feed.RemoveAt(0);
      }
      return true;
    } catch { return false; }
  }

  static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

  // ------- pintura -------
  static GraphicsPath RoundedPath(float x, float y, float w, float h, float r) {
    var p = new GraphicsPath(); float d = r * 2;
    p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure(); return p;
  }
  Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
  SolidBrush B(Color c) { int k = c.ToArgb(); if (!brushes.ContainsKey(k)) brushes[k] = new SolidBrush(c); return brushes[k]; }
  // cor interpolada pelo CALOR do modo (0=normal, 1=FABLE): a transicao recolore tudo junto
  Color HC(Color normal, Color myth) { return heat <= 0 ? normal : heat >= 1 ? myth : Lerp(normal, myth, heat); }

  // cadencia do nucleo: 30fps FIXO (33ms). 30 e divisor exato de 60Hz -> cada quadro dura
  // 2 refreshes = SEM judder (40fps bateria contra os 60Hz e engasga; 60fps seria liso mas
  // custa o dobro -- exagero p/ PC fraco). Com o timer de alta-res, 33ms sai regular e liso.
  int CoreInterval() { return 33; }

  // refaz o fundo cacheado (painel inteiro MENOS o nucleo). So roda quando os dados mudam
  // (1x/s no DataTick, ou ao sair de transicao/ignicao) -> o Render() pesado NAO repete por quadro.
  void RebuildBg() {
    if (bgBmp == null) bgBmp = new Bitmap(W, H);
    using (var g = Graphics.FromImage(bgBmp)) RenderPanel(g, false);
    bgDirty = false;
  }

  protected override void OnPaint(PaintEventArgs e) {
    try {
      if (closing) { PaintShutdown(e.Graphics); return; }
      if (booting) { PaintBoot(e.Graphics); return; }
      if (morphing) { PaintMorph(e.Graphics); return; }   // colapso/expansao da mini-capsula
      if (minimized) { PaintMini(e); return; }            // mini-capsula reator no canto
      if (inTrans) { Render(e.Graphics); return; }   // transicao: painel inteiro anima (one-shot, sem cache)
      // regime normal: blita a regiao invalidada do FUNDO cacheado + desenha o nucleo vivo por cima.
      // Assim o unico trabalho por quadro (a 30/60fps) e um blit barato + o nucleo -> fim do "travado".
      if (bgBmp == null || bgDirty) RebuildBg();
      var g2 = e.Graphics;
      Rectangle clip = e.ClipRectangle;
      if (clip.Width <= 0 || clip.Height <= 0 || clip.Width > W) clip = new Rectangle(0, 0, W, H);
      var prev = g2.CompositingMode;
      g2.CompositingMode = CompositingMode.SourceCopy;                 // copia crua 1:1 (sem alpha) = rapido
      g2.DrawImage(bgBmp, clip, clip, GraphicsUnit.Pixel);
      g2.CompositingMode = prev;
      g2.SmoothingMode = SmoothingMode.AntiAlias;
      g2.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
      DrawCore(g2, PAD + 36, 88);                                      // nucleo vivo (unico item a 30/60fps)
      if (fxs.Count > 0 && clip.Width > W - 20) DrawFx(g2);            // FX de evento (so no quadro cheio)
    } catch { /* 1 frame ruim nunca pode travar/derrubar a janela */ }
  }

  // ------- FX cinematicos por evento -------
  void AddFx(int type, int dur) { var f = new Fx(); f.type = type; f.start = NowMs(); f.dur = dur; fxs.Add(f); if (fxs.Count > 6) fxs.RemoveAt(0); }
  bool FxActive() { long n = NowMs(); for (int i = 0; i < fxs.Count; i++) if (n - fxs[i].start < fxs[i].dur) return true; return false; }
  void DrawFx(Graphics g) {
    long n = NowMs(); float cx = PAD + 36, cy = 88;
    for (int i = fxs.Count - 1; i >= 0; i--) {
      var f = fxs[i];
      double t = (double)(n - f.start) / f.dur;
      if (t >= 1) { fxs.RemoveAt(i); continue; }
      if (t < 0) continue;
      if (f.type == FX_RIPPLE) DrawRippleFx(g, cx, cy, t);
      else if (f.type == FX_BURST) DrawBurstFx(g, cx, cy, t);
      else if (f.type == FX_FLASH) DrawFlashFx(g, t);
    }
  }
  // ONDA DE CHOQUE: anel expandindo do nucleo a cada telemetria nova (a sessao "pulsa" viva)
  void DrawRippleFx(Graphics g, float cx, float cy, double t) {
    double e = 1 - (1 - t) * (1 - t);
    float r = (float)(18 + e * 300);
    int a = (int)(130 * (1 - t) * (1 - t));
    if (a > 0) using (var p = new Pen(Color.FromArgb(a, HC(Amber, MythGold)), (float)(2.6 * (1 - t) + 0.5))) g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
    float r2 = r * 0.72f; int a2 = (int)(a * 0.5);
    if (a2 > 0) using (var p2 = new Pen(Color.FromArgb(a2, HC(AmberBright, MythPale)), 1f)) g.DrawEllipse(p2, cx - r2, cy - r2, 2 * r2, 2 * r2);
  }
  // EXPLOSAO DOURADA: raios + clarao central ao concluir uma tarefa (100%)
  void DrawBurstFx(Graphics g, float cx, float cy, double t) {
    double e = 1 - (1 - t) * (1 - t);
    int a = (int)(220 * (1 - t));
    for (int k = 0; k < 12; k++) {
      double ang = k * Math.PI / 6 + 0.26;
      float r0 = (float)(10 + e * 40), r1 = (float)(20 + e * 150);
      using (var p = new Pen(Color.FromArgb(a, 255, 224, 150), (float)(2.2 * (1 - t) + 0.4))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, cx + (float)(Math.Cos(ang) * r0), cy + (float)(Math.Sin(ang) * r0), cx + (float)(Math.Cos(ang) * r1), cy + (float)(Math.Sin(ang) * r1)); }
    }
    float gr = (float)(18 + e * 24); int ga = (int)(160 * (1 - t) * (1 - t));
    if (ga > 0) using (var gp = new GraphicsPath()) { gp.AddEllipse(cx - gr, cy - gr, 2 * gr, 2 * gr); using (var pgb = new PathGradientBrush(gp)) { pgb.CenterColor = Color.FromArgb(ga, 255, 246, 210); pgb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 214, 138) }; g.FillPath(pgb, gp); } }
  }
  // FLASH DE ENERGIZACAO: pulso quente vindo das bordas quando a sessao volta a OPERAR
  void DrawFlashFx(Graphics g, double t) {
    int a = (int)(60 * Math.Sin(Math.PI * t));
    if (a <= 0) return;
    using (var gp = new GraphicsPath()) {
      gp.AddEllipse(-W * 0.3f, -H * 0.4f, W * 1.6f, H * 1.8f);
      using (var pgb = new PathGradientBrush(gp)) {
        pgb.CenterPoint = new PointF(W / 2f, H * 0.42f);
        pgb.CenterColor = Color.FromArgb(0, 0, 0, 0);
        pgb.SurroundColors = new Color[] { Color.FromArgb(a, HC(Amber, Ember)) };
        g.FillPath(pgb, gp);
      }
    }
  }

  // ------- MINIMIZAR: morph (implosao/expansao) + mini-capsula reator -------
  int CurW() { return minimized ? MINI_W : W; }
  int CurH() { return minimized ? MINI_H : H; }
  // raio dos cantos interpolado pela altura: 16 (cheio) -> 27 (pilula mini)
  float RegionRad(int h) {
    double p = (double)(H - h) / (H - MINI_H); if (p < 0) p = 0; if (p > 1) p = 1;
    return (float)(RAD + (MINI_H / 2.0 - RAD) * p);
  }
  static ColorMatrix AlphaMatrix(double a) { if (a < 0) a = 0; if (a > 1) a = 1; var m = new ColorMatrix(); m.Matrix33 = (float)a; return m; }

  void BeginMorph(bool toMini) {
    if (morphing) return;
    if (inTrans) { heat = heatTo; inTrans = false; }                      // completa transicao de modo pendente (senao minimizar a deixa presa na pele errada, achado #3)
    bool wasDragged = userMoved;                                          // ANTES de zerar: p/ decidir a pegada reservada
    morphing = true; morphDir = toMini ? 1 : -1; morphStart = NowMs();
    morphStartRect = Bounds;                                              // de onde a janela parte (coord de tela)
    if (toMini) userMoved = false;                                        // minimizar rejunta ao auto-layout (estaciona no dock)
    if (toMini && wasDragged) {
      // minimizar uma janela ARRASTADA: entra JA como MINI no dock (reserva mini, sem inchar a
      // pegada cheia que seria desfeita 300ms depois -> sem solavanco duplo nas vizinhas, achado #7)
      Point p = HudLayout.Place(pid, bornMs, MINI_W, MINI_H, false, true);
      morphDestRect = new Rectangle(p.X, p.Y, MINI_W, MINI_H);
    } else {
      // RESERVA a pegada CHEIA no dock durante TODO o morph -> as vizinhas nunca invadem:
      // expandir cresce em espaco que elas ja liberaram; minimizar so as deixa fechar no EndMorph.
      Point anchor;
      if (!userMoved && !dragging) anchor = HudLayout.Place(pid, bornMs, W, H, false, false);   // escreve slot CHEIO + devolve a ancora do dock
      else anchor = new Point(Location.X + Width - W, Location.Y);        // arrastada no EXPAND: cresce no lugar, canto dir fixo
      morphDestRect = toMini ? new Rectangle(anchor.X + W - MINI_W, anchor.Y, MINI_W, MINI_H)   // mini encostada na direita, mesmo topo
                             : new Rectangle(anchor.X, anchor.Y, W, H);
    }
    try { fullShot = new Bitmap(W, H); using (var g = Graphics.FromImage(fullShot)) Render(g); } catch { fullShot = null; }
    try { miniShotBmp = new Bitmap(MINI_W, MINI_H); using (var g = Graphics.FromImage(miniShotBmp)) RenderMini(g); } catch { miniShotBmp = null; }
    if (animTimer != null) animTimer.Interval = 8;                        // morph liso ~120fps
  }
  void ApplyMorphBounds(double t) {
    double e = SmoothStep(t);                                             // 0=inicio (start) .. 1=fim (dest)
    int x = (int)Math.Round(morphStartRect.X + (morphDestRect.X - morphStartRect.X) * e);
    int y = (int)Math.Round(morphStartRect.Y + (morphDestRect.Y - morphStartRect.Y) * e);
    int w = (int)Math.Round(morphStartRect.Width + (morphDestRect.Width - morphStartRect.Width) * e);
    int h = (int)Math.Round(morphStartRect.Height + (morphDestRect.Height - morphStartRect.Height) * e);
    try { SetBounds(x, y, w, h); using (var gp = RoundedPath(0, 0, w, h, RegionRad(h))) Region = new Region(gp); } catch {}
  }
  void EndMorph() {
    morphing = false; minimized = (morphDir == 1);
    int w = CurW(), h = CurH();
    int nx = morphDestRect.X, ny = morphDestRect.Y;
    // slot FINAL (mini no minimizar, cheio no expandir): so agora as vizinhas fecham o espaco reservado
    if (!userMoved && !dragging) { var np = HudLayout.Place(pid, bornMs, w, h, false, minimized); nx = np.X; ny = np.Y; }
    try { SetBounds(nx, ny, w, h); using (var gp = RoundedPath(0, 0, w, h, RegionRad(h))) Region = new Region(gp); } catch {}
    try { if (fullShot != null) { fullShot.Dispose(); fullShot = null; } } catch {}
    try { if (miniShotBmp != null) { miniShotBmp.Dispose(); miniShotBmp = null; } } catch {}
    bgDirty = true; miniBgDirty = true;
    if (animTimer != null && !closing) animTimer.Interval = CoreInterval();
    Invalidate();
  }
  // dissolve cruzado + escala geometrica: o painel cheio some encolhendo enquanto a
  // mini-capsula surge (e vice-versa). Reusa a linguagem de snapshot da ignicao/desligamento.
  void PaintMorph(Graphics g) {
    double t = (NowMs() - morphStart) / (double)MORPH_MS; if (t < 0) t = 0; if (t > 1) t = 1;
    double p = morphDir == 1 ? SmoothStep(t) : SmoothStep(1 - t);
    int w = Width, h = Height;
    g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.Bilinear;
    using (var bgb = new SolidBrush(Ink2)) using (var gp = RoundedPath(0, 0, w, h, RegionRad(h))) g.FillPath(bgb, gp);
    if (fullShot != null && p < 0.98) using (var ia = new ImageAttributes()) { ia.SetColorMatrix(AlphaMatrix(1 - p)); g.DrawImage(fullShot, new Rectangle(0, 0, w, h), 0, 0, W, H, GraphicsUnit.Pixel, ia); }
    if (miniShotBmp != null && p > 0.02) using (var ia = new ImageAttributes()) { ia.SetColorMatrix(AlphaMatrix(p)); g.DrawImage(miniShotBmp, new Rectangle(0, 0, w, h), 0, 0, MINI_W, MINI_H, GraphicsUnit.Pixel, ia); }
  }

  void PaintMini(PaintEventArgs e) {
    if (miniBg == null || miniBgDirty) { if (miniBg == null) miniBg = new Bitmap(MINI_W, MINI_H); using (var g0 = Graphics.FromImage(miniBg)) RenderMiniBg(g0); miniBgDirty = false; }
    var g = e.Graphics;
    var prev = g.CompositingMode; g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(miniBg, 0, 0); g.CompositingMode = prev;
    g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    DrawMiniCore(g, 28, MINI_H / 2f);
  }
  // snapshot completo do mini (fundo + nucleo) usado pelo morph
  void RenderMini(Graphics g) { RenderMiniBg(g); g.SmoothingMode = SmoothingMode.AntiAlias; DrawMiniCore(g, 28, MINI_H / 2f); }

  void RenderMiniBg(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    var rect = new Rectangle(0, 0, MINI_W, MINI_H);
    using (var bg = new LinearGradientBrush(rect, HC(Ink1, InkF1), HC(Ink2, InkF2), 60f)) using (var gp = RoundedPath(0, 0, MINI_W, MINI_H, MINI_H / 2f)) g.FillPath(bg, gp);
    double hk = 0.5 + 0.5 * Math.Sin(NowMs() / 800.0);
    using (var gp = RoundedPath(0.7f, 0.7f, MINI_W - 1.4f, MINI_H - 1.4f, (MINI_H - 1.4f) / 2f))
    using (var pen = new Pen(Color.FromArgb((int)(200 + 25 * heat), HC(BorderC, Lerp(MythGold, Ember, hk * 0.7))), 1.3f)) g.DrawPath(pen, gp);
    float tx = 54;
    Color sc = status == "OPERANDO" ? HC(Amber, Ember) : status == "ENCERRADO" ? Faint : Online;
    string t1 = string.IsNullOrEmpty(title) ? "J.A.R.V.I.S." : title;
    g.DrawString(Fit(g, t1, fSess, MINI_W - tx - 20), fSess, B(HC(Amber, MythGold)), tx, 8);   // recuado p/ o botao fechar
    string t2 = (heat >= 0.5 && status == "OPERANDO" ? "PLENA CARGA" : status) + "  " + Elapsed();
    g.DrawString(Fit(g, t2, fTiny, MINI_W - tx - 14), fTiny, B(sc), tx, 27);
    // botao FECHAR (x) no topo-direito da capsula
    g.DrawString("x", fTiny, B(AmberMut), miniCloseRect.X + 1, miniCloseRect.Y - 2);
    g.DrawImage(Cine.Overlay(MINI_W, MINI_H), rect);
  }

  // mini reator: versao enxuta do nucleo (le bem em ~40px) — anel + ticks + arco + raios + core.
  void DrawMiniCore(Graphics g, float cx, float cy) {
    float a = (float)spin; double energy = CoreEnergy();
    double pulse = 0.5 + 0.5 * Math.Sin(phase * (2.2 + 1.6 * energy));
    Color cMain = HC(Amber, MythGold), cHot = HC(AmberBright, MythPale);
    using (var gl = new SolidBrush(Color.FromArgb(Clamp255((int)(38 + 26 * pulse + 26 * energy)), cHot))) g.FillEllipse(gl, cx - 16, cy - 16, 32, 32);
    using (var p = new Pen(Color.FromArgb(60, cMain), 1f)) g.DrawEllipse(p, cx - 18, cy - 18, 36, 36);
    var st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(a);
    using (var p = new Pen(Color.FromArgb(150, cMain), 1.6f)) { for (int i = 0; i < 8; i++) { double r = i * Math.PI / 4; g.DrawLine(p, (float)(Math.Cos(r) * 14), (float)(Math.Sin(r) * 14), (float)(Math.Cos(r) * 17.5), (float)(Math.Sin(r) * 17.5)); } }
    g.Restore(st);
    st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(-a * 1.4f);
    using (var p = new Pen(Color.FromArgb(210, cHot), 1.8f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, -12, -12, 24, 24, 20, 130); }
    g.Restore(st);
    st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(a * 0.55f);
    for (int i = 0; i < 8; i++) { double ang = i * Math.PI / 4; double len = 8 + pulse * 2.5; using (var p = new Pen(Color.FromArgb(Clamp255((int)(170 * (0.6 + 0.4 * pulse))), (i % 2 == 0) ? cHot : cMain), 1.3f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, (float)(Math.Cos(ang) * 2.5), (float)(Math.Sin(ang) * 2.5), (float)(Math.Cos(ang) * len), (float)(Math.Sin(ang) * len)); } }
    g.Restore(st);
    float gr = 6f + (float)(pulse * 2.5);
    using (var gpath = new GraphicsPath()) { gpath.AddEllipse(cx - gr, cy - gr, 2 * gr, 2 * gr); using (var pgb = new PathGradientBrush(gpath)) { pgb.CenterPoint = new PointF(cx, cy); pgb.CenterColor = Color.FromArgb(Clamp255((int)(160 + 70 * pulse + 30 * energy)), 255, 244, 214); pgb.SurroundColors = new Color[] { Color.FromArgb(0, cMain) }; g.FillPath(pgb, gpath); } }
    float pr = (float)(2.6 + 0.7 * energy) + (float)(pulse * 1.2);
    using (var b = new SolidBrush(cMain)) g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);
    using (var b = new SolidBrush(Lerp(Color.FromArgb(255, 255, 248, 232), Color.White, heat))) g.FillEllipse(b, cx - 1.5f, cy - 1.5f, 3f, 3f);
  }

  // ------- DESLIGAMENTO ANIMADO: esfria o nucleo e colapsa a tela estilo CRT -------
  // Congela o ultimo frame vivo num bitmap e o esfria/colapsa por cima. Um so disparo
  // (nao e animacao continua): so roda quando a telinha vai fechar.
  void BeginShutdown() {
    if (closing) return;
    if (minimized || morphing) { RealClose(); return; }                       // mini nao tem espaco p/ o CRT: fecha quieto
    if (booting) {                                                             // desligar vence a ignicao
      booting = false;
      try { if (bootBmp != null) { bootBmp.Dispose(); bootBmp = null; } } catch {}
    }
    closing = true; closeStartMs = NowMs();
    try {
      shotBmp = new Bitmap(W, H); using (var g = Graphics.FromImage(shotBmp)) Render(g);
      coldBmp = new Bitmap(W, H);                                              // versao JA FRIA (p/ o colapso)
      using (var g = Graphics.FromImage(coldBmp)) using (var ia = new ImageAttributes()) {
        ia.SetColorMatrix(CoolMatrix(1.0));
        g.DrawImage(shotBmp, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel, ia);
      }
    } catch { shotBmp = null; coldBmp = null; }
    if (dataTimer != null) dataTimer.Stop();                                   // congela os dados
    if (layoutTimer != null) layoutTimer.Stop();                               // para a recompactacao de layout no desligamento
    if (animTimer != null) { animTimer.Interval = 8; if (!animTimer.Enabled) animTimer.Start(); }  // ~120fps (alta-res ja segurada)
    Invalidate();
  }
  void RealClose() {
    try { if (shotBmp != null) { shotBmp.Dispose(); shotBmp = null; } if (coldBmp != null) { coldBmp.Dispose(); coldBmp = null; } } catch {}
    Close();
  }

  static double SmoothStep(double t) { if (t < 0) t = 0; if (t > 1) t = 1; return t * t * (3 - 2 * t); }   // suave nas pontas
  static Color Lerp(Color a, Color b, double t) {
    if (t < 0) t = 0; if (t > 1) t = 1;
    return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
  }
  // matriz de cor p/ recolorir a tela inteira: identidade -> FRIO (desatura + tinte azulado + escurece)
  static ColorMatrix CoolMatrix(double s) {
    if (s < 0) s = 0; if (s > 1) s = 1;
    double lr = 0.3, lg = 0.59, lb = 0.11;              // luminancia
    double cr = 0.5, cg = 0.78, cb = 1.2, d = 0.6;      // tinte frio (azulado) * escurecimento
    double[,] cold = new double[5, 5];
    cold[0, 0] = lr * cr * d; cold[0, 1] = lr * cg * d; cold[0, 2] = lr * cb * d;
    cold[1, 0] = lg * cr * d; cold[1, 1] = lg * cg * d; cold[1, 2] = lg * cb * d;
    cold[2, 0] = lb * cr * d; cold[2, 1] = lb * cg * d; cold[2, 2] = lb * cb * d;
    cold[3, 3] = 1; cold[4, 4] = 1;
    double[,] id = new double[5, 5]; id[0, 0] = id[1, 1] = id[2, 2] = id[3, 3] = id[4, 4] = 1;
    float[][] m = new float[5][];
    for (int i = 0; i < 5; i++) { m[i] = new float[5]; for (int j = 0; j < 5; j++) m[i][j] = (float)(id[i, j] * (1 - s) + cold[i, j] * s); }
    return new ColorMatrix(m);
  }

  // ------- IGNICAO ANIMADA: fisga de luz -> linha -> abertura CRT -> aquecimento -------
  // Mesma linguagem visual do desligamento, invertida e QUENTE: o painel nasce gelido
  // e escuro, as cores acendem ate o regime pleno e o nucleo da um flare de partida.
  void PaintBoot(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.Bilinear;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    double p = (double)(NowMs() - bootStartMs) / BOOT_MS; if (p < 0) p = 0; if (p > 1) p = 1;
    using (var bgb = new SolidBrush(Ink2)) g.FillRectangle(bgb, 0, 0, W, H);
    float cy = H / 2f;
    Color warm = heat >= 0.5 ? MythGold : AmberBright;
    Color pale = heat >= 0.5 ? MythPale : Color.FromArgb(255, 255, 244, 214);

    if (p < 0.10) {                              // ---- 1) FISGA: um ponto de luz acende ----
      double t = p / 0.10;
      float r = (float)(1.5 + 6 * t);
      using (var gl = new SolidBrush(Color.FromArgb((int)(150 * t), warm))) g.FillEllipse(gl, W / 2f - r * 2.2f, cy - r * 2.2f, r * 4.4f, r * 4.4f);
      using (var dot = new SolidBrush(Color.FromArgb((int)(255 * t), pale))) g.FillEllipse(dot, W / 2f - r, cy - r, r * 2, r * 2);
      return;
    }
    if (p < 0.26) {                              // ---- 2) PONTO -> LINHA ----
      double t = (p - 0.10) / 0.16; double e2 = 1 - (1 - t) * (1 - t);
      float ww = (float)(e2 * W); float x = (W - ww) / 2f;
      using (var glow = new SolidBrush(Color.FromArgb((int)(120 * (1 - 0.4 * t)), warm))) g.FillRectangle(glow, x, cy - 6, ww, 12);
      using (var line = new SolidBrush(pale)) g.FillRectangle(line, x, cy - 1.5f, ww, 3f);
      return;
    }
    if (bootBmp == null) return;
    if (p < 0.54) {                              // ---- 3) ABERTURA VERTICAL (CRT ao contrario) ----
      double t = (p - 0.26) / 0.28; double e2 = 1 - (1 - t) * (1 - t);
      float hh = (float)(e2 * H); if (hh < 3) hh = 3;
      float y = cy - hh / 2f;
      using (var ia = new ImageAttributes()) {
        ia.SetColorMatrix(CoolMatrix(0.85 * (1 - t)));           // abre gelido e ja vai esquentando
        g.DrawImage(bootBmp, new Rectangle(0, (int)y, W, (int)hh), 0, 0, W, H, GraphicsUnit.Pixel, ia);
      }
      int la = (int)(230 * (1 - t));
      using (var glow = new SolidBrush(Color.FromArgb((int)(110 * (1 - t)), warm))) g.FillRectangle(glow, 0, y - 7, W, 14);
      using (var line = new SolidBrush(Color.FromArgb(la, pale))) { g.FillRectangle(line, 0, y - 1.5f, W, 3f); g.FillRectangle(line, 0, y + hh - 1.5f, W, 3f); }
      return;
    }
    {                                            // ---- 4) AQUECIMENTO + FLARE DO NUCLEO ----
      double t = (p - 0.54) / 0.46;
      double s = 0.85 * (1 - Math.Min(1.0, t * 1.35));           // frio residual -> zero (cores plenas)
      if (s > 0.004) {
        using (var ia = new ImageAttributes()) { ia.SetColorMatrix(CoolMatrix(s)); g.DrawImage(bootBmp, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel, ia); }
      } else g.DrawImage(bootBmp, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel);
      float sy = (float)((1 - t) * (H + 16)) - 8;                // varredura de energizacao SUBINDO
      using (var sl = new SolidBrush(Color.FromArgb((int)(70 * (1 - t)), warm))) g.FillRectangle(sl, 0, sy - 1, W, 2.5f);
      double fk = Math.Sin(Math.PI * Math.Min(1.0, t / 0.75));   // flare do nucleo: incha e assenta
      if (fk > 0.01) {
        float cxr = PAD + 36, cyr = 88;
        float fr = (float)(10 + 46 * t);
        using (var fg = new SolidBrush(Color.FromArgb((int)(95 * fk), warm))) g.FillEllipse(fg, cxr - fr, cyr - fr, 2 * fr, 2 * fr);
        using (var fp = new Pen(Color.FromArgb((int)(140 * fk), pale), 1.6f)) g.DrawEllipse(fp, cxr - fr * 0.72f, cyr - fr * 0.72f, fr * 1.44f, fr * 1.44f);
      }
      string msg = t < 0.5 ? (heat >= 0.5 ? "// PROTOCOLO FABLE 5" : "// IGNICAO DO NUCLEO") : "// SISTEMAS ONLINE";
      int ta = (int)(205 * (1 - t * t));
      if (ta > 0) using (var b = new SolidBrush(Color.FromArgb(ta, Lerp(AmberMut, Online, t)))) g.DrawString(msg, fLbl, b, PAD - 1, H - 26);
    }
  }

  void PaintShutdown(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.Bilinear;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    double p = (double)(NowMs() - closeStartMs) / SHUT_MS; if (p < 0) p = 0; if (p > 1) p = 1;
    using (var bgb = new SolidBrush(Ink2)) g.FillRectangle(bgb, 0, 0, W, H);   // reator apagado = fundo escuro
    if (shotBmp == null) return;
    float cy = H / 2f;

    if (p < 0.44) {                              // ---- 1) ESFRIANDO: os ELEMENTOS recolorem (ambar -> frio) ----
      double t = SmoothStep(p / 0.44);
      using (var ia = new ImageAttributes()) {
        ia.SetColorMatrix(CoolMatrix(t));        // recolore TUDO fluidamente (nao e so um veu por cima)
        g.DrawImage(shotBmp, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel, ia);
      }
      // glitch CRT: fatias finas do frame QUENTE saltam por um instante (energia resistindo)
      if ((p * 9.0) % 1.0 < 0.16) {
        float gy1 = (float)(((p * 57.0) % 1.0) * (H - 40)) + 14;
        float gdx = (float)(Math.Sin(p * 400.0) * 5.0);
        g.DrawImage(shotBmp, new RectangleF(gdx, gy1, W, 5), new RectangleF(0, gy1, W, 5), GraphicsUnit.Pixel);
        g.DrawImage(shotBmp, new RectangleF(-gdx, gy1 + 9, W, 3), new RectangleF(0, gy1 + 9, W, 3), GraphicsUnit.Pixel);
      }
      float coreX = PAD + 36, coreY = 88; double ember = 1.0 - t;                // brasa do reator morrendo por cima
      Color emc = Lerp(Amber, Red, Math.Min(1.0, t * 1.5));
      float er = (float)(22 * ember) + 3f;
      using (var gl = new SolidBrush(Color.FromArgb((int)(120 * ember), emc))) g.FillEllipse(gl, coreX - er, coreY - er, 2 * er, 2 * er);
      float sy = (float)(t * (H + 20)) - 10;                                     // varredura de power-down descendo
      using (var sl = new SolidBrush(Color.FromArgb((int)(80 * (1 - t)), 150, 210, 245))) g.FillRectangle(sl, 0, sy - 1, W, 2.5f);
      string msg = t < 0.55 ? "// RESFRIANDO O NUCLEO" : "// SISTEMAS OFFLINE";
      using (var b = new SolidBrush(Color.FromArgb((int)(205 * (1 - 0.3 * t)), Lerp(AmberMut, C("#7FA8C8"), t)))) g.DrawString(msg, fLbl, b, PAD - 1, H - 26);
      return;
    }
    Bitmap src = coldBmp != null ? coldBmp : shotBmp;
    if (p < 0.80) {                              // ---- 2) COLAPSO VERTICAL (CRT) ----
      double t = (p - 0.44) / 0.36;
      float hh = (float)((1 - t * t) * H); if (hh < 2) hh = 2;                   // easeIn (acelera)
      float y = cy - hh / 2f;
      g.DrawImage(src, new RectangleF(0, y, W, hh), new RectangleF(0, 0, W, H), GraphicsUnit.Pixel);   // esmaga em Y (ja frio)
      using (var glow = new SolidBrush(Color.FromArgb((int)(145 * t), 150, 210, 245))) g.FillRectangle(glow, 0, cy - 9, W, 18);   // linha CRT intensificando
      using (var line = new SolidBrush(Color.FromArgb(255, 210, 238, 255))) g.FillRectangle(line, 0, cy - 1.5f, W, 3f);
      return;
    }
    if (p < 0.93) {                              // ---- 3) LINHA -> PONTO ----
      double t = (p - 0.80) / 0.13;
      float ww = (float)((1 - t * t) * W); if (ww < 4) ww = 4;
      float x = (W - ww) / 2f;
      using (var glow = new SolidBrush(Color.FromArgb((int)(130 * (1 - t)), 150, 210, 245))) g.FillRectangle(glow, x, cy - 7, ww, 14);
      using (var line = new SolidBrush(Color.FromArgb(255, 224, 242, 255))) g.FillRectangle(line, x, cy - 1.5f, ww, 3f);
      return;
    }
    {                                            // ---- 4) FLASH DO PONTO -> APAGA ----
      double t = (p - 0.93) / 0.07;
      float r = (float)(7 * (1 - t)) + 1f;
      using (var gl = new SolidBrush(Color.FromArgb((int)(170 * (1 - t)), 150, 210, 245))) g.FillEllipse(gl, W / 2f - r * 2, cy - r * 2, r * 4, r * 4);
      using (var dot = new SolidBrush(Color.FromArgb((int)(255 * (1 - t)), 240, 248, 255))) g.FillEllipse(dot, W / 2f - r, cy - r, r * 2, r * 2);
    }
  }

  // Pintura completa do painel. Usada pelo modo --shot, pela ignicao/desligamento (que
  // congelam 1 frame) e pela transicao de modo (painel inteiro anima). Em REGIME NORMAL
  // NAO e chamada por quadro: o OnPaint blita o fundo cacheado + desenha so o nucleo.
  // `drawCore=false` gera o FUNDO cacheado (tudo menos o nucleo).
  public void Render(Graphics g) { RenderPanel(g, true); }
  void RenderPanel(Graphics g, bool drawCore) {
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    var rect = new Rectangle(0, 0, W, H);
    // FABLE 5 em OVERHEAT: o fundo esquenta (tinta escura com brasa) e a moldura pulsa
    // entre ouro e brasa. Tudo interpola pelo `heat` -> a transicao recolore fluida.
    using (var bg = new LinearGradientBrush(rect, HC(Ink1, InkF1), HC(Ink2, InkF2), 72f)) g.FillRectangle(bg, rect);
    if (inTrans) {                                   // tranco fisico no estalo da troca de modo
      double stt = (NowMs() - transStart) / (double)TRANS_MS;
      if (stt >= 0 && stt < 0.24) {
        double sk = 1 - stt / 0.24;
        g.TranslateTransform((float)(Math.Sin(stt * 92) * 2.4 * sk), (float)(Math.Cos(stt * 71) * 1.7 * sk));
      }
    }
    // grade blueprint faint (profundidade de painel tecnico; some sob o recorte do nucleo)
    using (var gpen = new Pen(Color.FromArgb(9, HC(BorderC, MythGold)), 1f)) {
      for (int gx = 28; gx < W - 8; gx += 32) g.DrawLine(gpen, gx, 10, gx, H - 10);
      for (int gy = 42; gy < H - 8; gy += 32) g.DrawLine(gpen, 10, gy, W - 10, gy);
    }
    double hk = 0.5 + 0.5 * Math.Sin(NowMs() / 800.0);   // pulso de calor (avanca nos passos de 1s)
    using (var gp = RoundedPath(0.7f, 0.7f, W - 1.4f, H - 1.4f, RAD))
    using (var pen = new Pen(Color.FromArgb((int)(210 + 25 * heat), HC(BorderC, Lerp(MythGold, Ember, hk * 0.7))), 1.3f)) g.DrawPath(pen, gp);
    {   // cantoneiras de mira: segundo traco so nos 4 cantos (linguagem de visor)
      float o = 5.5f, dd = (RAD - 4) * 2;
      using (var bp = new Pen(Color.FromArgb((int)(70 + 40 * heat * hk), HC(BorderC, MythGold)), 1.3f)) {
        g.DrawArc(bp, o, o, dd, dd, 175, 100);
        g.DrawArc(bp, W - o - dd, o, dd, dd, 265, 100);
        g.DrawArc(bp, W - o - dd, H - o - dd, dd, dd, -5, 100);
        g.DrawArc(bp, o, H - o - dd, dd, dd, 85, 100);
      }
    }

    // cabecalho
    g.DrawString("J.A.R.V.I.S.", fTitle, B(Amber), PAD - 2, 11);
    if (heat > 0.02) DrawFableBadge(g, PAD - 2 + g.MeasureString("J.A.R.V.I.S.", fTitle).Width + 2, 12);
    Color sc = status == "OPERANDO" ? HC(Amber, Ember) : status == "ENCERRADO" ? Faint : Online;
    string stxt = (heat >= 0.5 && status == "OPERANDO") ? "PLENA CARGA" : status;
    float sw = g.MeasureString(stxt, fStat).Width;
    float sx = W - 56 - sw;                                          // recuado p/ abrir espaco aos 2 botoes (minimizar/fechar)
    using (var pillP = RoundedPath(sx - 18, 12, sw + 24, 14, 7)) {   // capsula do status
      using (var pf = new SolidBrush(Color.FromArgb(26, sc))) g.FillPath(pf, pillP);
      using (var pp = new Pen(Color.FromArgb(70, sc), 1f)) g.DrawPath(pp, pillP);
    }
    double dk = 0.5 + 0.5 * Math.Sin(NowMs() / 900.0);               // respiro do LED (passos de 1s)
    using (var hb2 = new SolidBrush(Color.FromArgb((int)(40 + 55 * dk), sc))) g.FillEllipse(hb2, sx - 14, 13, 11, 11);
    using (var b = new SolidBrush(sc)) g.FillEllipse(b, sx - 12, 15, 7, 7);
    g.DrawString(stxt, fStat, B(sc), sx, 12);
    // ---- botoes RECOLHER TUDO (chevron duplo), MINIMIZAR (–) e FECHAR (x), em ambar visivel ----
    using (var mp = new Pen(AmberMut, 2f)) { mp.StartCap = LineCap.Round; mp.EndCap = LineCap.Round; g.DrawLine(mp, minRect.X + 3, minRect.Y + 9, minRect.X + 13, minRect.Y + 9); }
    g.DrawString("x", fClose, B(AmberMut), closeRect.X + 2, closeRect.Y - 3);

    // "HA Xs" (frescor): responde travou-ou-pensando, quase gratis (1 subtracao/1 string)
    string idleTxt = IdleText();
    if (idleTxt.Length > 0) {
      Color ic = IdleColor();
      float iw = g.MeasureString(idleTxt, fTiny).Width;
      g.DrawString(idleTxt, fTiny, B(ic), sx - 26 - iw, 14);
    }

    // sessao + etiqueta do modelo real (discreta, a direita; Fable usa o badge do titulo)
    float mlw = 0;
    if (modelShort.Length > 0 && heat < 0.5) {
      mlw = g.MeasureString(modelShort, fTiny).Width;
      g.DrawString(modelShort, fTiny, B(Faint), W - PAD - mlw + 2, 34);
      mlw += 10;
    }
    string slabel = string.IsNullOrEmpty(title) ? "sessao (CLI)" : "// " + title;
    g.DrawString(Fit(g, slabel, fSess, W - 2 * PAD - mlw), fSess, B(AmberMut), PAD - 1, 32);

    // nucleo do reator (animado) — pulado ao gerar o FUNDO cacheado (desenhado vivo no OnPaint)
    if (drawCore) DrawCore(g, PAD + 36, 88);

    // ---- coluna esquerda: TEMPO DE OP. + ACOES (Fable = numeros incandescentes) ----
    Color vBig = HC(Amber, MythGold), vSub = HC(AmberDeep, Ember);
    g.DrawString("TEMPO DE OP.", fLbl, B(Faint), MX, 50);
    g.DrawString(Elapsed(), fBig, B(vBig), MX - 1, 59);
    g.DrawString("ACOES", fLbl, B(Faint), MX, 84);
    g.DrawString(actions.ToString(), fBig2, B(vBig), MX - 1, 93);
    float aw = g.MeasureString(actions.ToString(), fBig2).Width;
    g.DrawString("ops", fTiny, B(vSub), MX + aw + 3, 102);

    // ---- coluna direita, linha 1: ATIVIDADE (tarefas) ou CARGA (reator) ----
    bool hasTasks = taskTotal > 0;
    g.DrawString(hasTasks ? "ATIVIDADE" : "CARGA", fLbl, B(Faint), X2, 50);
    using (var tb = new SolidBrush(Color.FromArgb(150, 6, 11, 8))) g.FillPath(tb, RoundedPath(BARX, BARY, BARW, BARH, BARH / 2f));
    using (var pen = new Pen(Color.FromArgb(70, BorderC), 1f)) g.DrawPath(pen, RoundedPath(BARX, BARY, BARW, BARH, BARH / 2f));
    float pct = hasTasks ? (float)taskDone / taskTotal : (float)loadPct;
    if (pct > 0) {
      float fw = Math.Max(BARH, BARW * pct);
      Color hot = HC(AmberBright, MythPale);                      // Fable: rampa fundida brasa -> ouro-branco
      Color deep = HC(AmberDeep, EmberDeep);
      Color g1 = hasTasks ? deep : Color.FromArgb(220, deep);
      Color g2 = hasTasks ? hot : Color.FromArgb(220, hot);
      using (var fill = new LinearGradientBrush(new RectangleF(BARX, BARY, BARW, BARH), g1, g2, 0f))
      using (var clip = RoundedPath(BARX, BARY, fw, BARH, BARH / 2f)) g.FillPath(fill, clip);
    }
    using (var gloss = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))       // vidro na barra
    using (var gpth = RoundedPath(BARX + 1, BARY + 1, BARW - 2, BARH / 2f, 3)) g.FillPath(gloss, gpth);
    if (!hasTasks && loadPeak > 0.04) {                                          // pico VU: segura e decai
      float pxk = BARX + (float)(BARW * loadPeak); if (pxk > BARX + BARW - 1) pxk = BARX + BARW - 1;
      using (var pk = new SolidBrush(Color.FromArgb(200, HC(AmberBright, MythPale)))) g.FillRectangle(pk, pxk - 1, BARY - 2, 2f, BARH + 4);
    }
    g.DrawString((int)Math.Round(pct * 100) + "%", fStat, B(pct > 0 ? vBig : Faint), BARX + BARW + 6, BARY - 4);
    g.DrawString(hasTasks ? (taskDone + "/" + taskTotal) : (loadPct > 0.05 ? (heat >= 0.5 ? "a plena carga" : "reator ativo") : "em repouso"), fTiny, B(vSub), BARX, BARY + 11);

    // ---- coluna direita, linha 2: APM (acoes/min) + tendencia + pico ----
    g.DrawString("APM", fLbl, B(Faint), X2, 84);
    string apmStr = apm.ToString();
    g.DrawString(apmStr, fBig2, B(apm > 0 ? vBig : Faint), X2 - 1, 93);
    float apmW = g.MeasureString(apmStr, fBig2).Width;
    DrawTrend(g, X2 + apmW + 6, 100);
    g.DrawString("pico " + apmPeak, fTiny, B(vSub), X2 + apmW + 20, 101);

    // ---- sparkline de PULSO (16 baldes ~4s = ~64s), full-width sob as metricas ----
    DrawSparkline(g);

    // divisor + cabecalho do feed
    using (var pen = new Pen(Color.FromArgb(48, BorderC), 1f)) g.DrawLine(pen, PAD, DIVY, W - PAD, DIVY);
    g.DrawString("// FLUXO DE TELEMETRIA", fLbl, B(Faint), PAD - 1, DIVY + 6);
    string ev = "EVENTOS " + actions.ToString("D4");
    g.DrawString(ev, fTiny, B(vSub), W - PAD - g.MeasureString(ev, fTiny).Width, DIVY + 7);

    // feed (ultimas N, mais nova embaixo); a fala do Jarvis (JVS) pisca ao chegar
    int start = Math.Max(0, feed.Count - FEEDN);
    float y = DIVY + 24;
    long now = NowMs();
    for (int i = start; i < feed.Count; i++) {
      var f = feed[i];
      Color tc = TagColor(f[1]);
      var chip = new RectangleF(PAD, y + 1, 40, 13);
      // flash efemero no chip JVS: halo alfa que decai em 3s (passos de 1s do data timer)
      if (f[1] == "JVS") {
        long fts = 0; long.TryParse(f[0], out fts);
        double k = 1.0 - (now - fts) / 3000.0;
        if (k > 0 && k <= 1) {
          using (var halo = new SolidBrush(Color.FromArgb((int)(110 * k), AmberBright)))
          using (var hp = RoundedPath(chip.X - 2, chip.Y - 2, chip.Width + 4, chip.Height + 4, 4)) g.FillPath(halo, hp);
        }
      }
      using (var cf = new SolidBrush(Color.FromArgb(20, tc)))                     // miolo sutil do chip
      using (var cfp = RoundedPath(chip.X, chip.Y, chip.Width, chip.Height, 3)) g.FillPath(cf, cfp);
      using (var cp = new Pen(Color.FromArgb(150, tc), 1f)) g.DrawPath(cp, RoundedPath(chip.X, chip.Y, chip.Width, chip.Height, 3));
      var cs = g.MeasureString(f[1], fChip);
      g.DrawString(f[1], fChip, B(tc), chip.X + (chip.Width - cs.Width) / 2, chip.Y + 1.5f);
      if (i == feed.Count - 1) {                                                  // accent da linha mais nova (fade 3s)
        long ats = 0; long.TryParse(f[0], out ats);
        double ak = 1.0 - (now - ats) / 3000.0;
        if (ak > 0 && ak <= 1) using (var ab = new SolidBrush(Color.FromArgb((int)(150 * ak), HC(AmberBright, MythPale)))) g.FillRectangle(ab, 7.5f, y + 1, 2f, 13f);
      }
      g.DrawString(Fit(g, f[2], fFeed, W - PAD - (PAD + 48)), fFeed, B(i == feed.Count - 1 ? TextC : AmberMut), PAD + 48, y - 1);
      y += 16.5f;
    }
    if (feed.Count == 0)
      g.DrawString("Aguardando telemetria da sessao...", fFeed, B(Faint), PAD + 2, DIVY + 27);

    if (drawCore && inTrans) DrawModelTrans(g);   // onda de choque + letreiro (so no caminho vivo, nao no fundo cacheado)
    g.ResetTransform();               // o "vidro" do painel nao treme junto com o conteudo
    g.DrawImage(Cine.Overlay(W, H), new Rectangle(0, 0, W, H));   // scanlines + vinheta (1 bitmap cacheado)
  }

  // ONDA DE TRANSICAO entre modos: flash no nucleo + 2 aneis de choque varrendo a
  // janela + letreiro do protocolo. Dourado-brasa ao ENGAJAR; azul-aco (mesma
  // linguagem do desligamento) ao RECOLHER. One-shot: so roda durante TRANS_MS.
  void DrawModelTrans(Graphics g) {
    double t = (NowMs() - transStart) / (double)TRANS_MS; if (t < 0) t = 0; if (t > 1) t = 1;
    bool up = heatTo > heatFrom;
    float cx = PAD + 36, cy = 88;
    Color wave = up ? Lerp(MythGold, Ember, 0.35) : Color.FromArgb(255, 150, 210, 245);
    for (int i = 0; i < 2; i++) {
      double tt = (t - i * 0.14) / 0.86; if (tt <= 0 || tt >= 1) continue;
      float r = (float)(10 + tt * 420);
      using (var p = new Pen(Color.FromArgb((int)(150 * (1 - tt)), wave), (float)(9 * (1 - tt) + 1.5)))
        g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
    }
    if (t < 0.24) {                                     // flash do nucleo no estalo da troca
      double k = 1 - t / 0.24;
      float fr = (float)(16 + 34 * (t / 0.24));
      using (var b = new SolidBrush(Color.FromArgb((int)(170 * k), up ? MythPale : Color.FromArgb(255, 210, 238, 255))))
        g.FillEllipse(b, cx - fr, cy - fr, 2 * fr, 2 * fr);
    }
    string msg = up ? "// ENGAJANDO PROTOCOLO FABLE 5" : "// FABLE 5 RECOLHIDO AO COFRE";
    int a2 = (int)(230 * Math.Sin(Math.PI * t));
    if (a2 > 0) using (var b = new SolidBrush(Color.FromArgb(a2, up ? MythPale : C("#9FC4DC"))))
      g.DrawString(msg, fLbl, b, PAD - 1, H - 26);
  }

  // sparkline: barras solidas, recalculadas 1x/s; ~16 FillRectangle (<1ms). Zero anim continua.
  void DrawSparkline(Graphics g) {
    int n = 16;
    float bw = 13f, gap = (SPKW - n * bw) / (n - 1);   // distribui na largura
    int smax = 1; for (int b = 0; b < n; b++) if (spark[b] > smax) smax = spark[b];
    for (int b = 0; b < n; b++) {
      float bx = SPKX + b * (bw + gap);
      int v = spark[b];
      if (v <= 0) {
        using (var fl = new SolidBrush(Color.FromArgb(70, Faint))) g.FillRectangle(fl, bx, SPKBASE - 2, bw, 2f);
        continue;
      }
      float h = Math.Max(3f, (v / (float)smax) * (SPKBASE - SPKTOP));
      Color cc = b == n - 1 ? (fable ? MythPale : AmberBright)
        : (b >= 11 ? (fable ? MythGold : Amber) : (b >= 6 ? (fable ? Ember : AmberMut) : (fable ? EmberDeep : AmberDeep)));
      if (b == n - 1) using (var hl = new SolidBrush(Color.FromArgb(46, cc))) g.FillRectangle(hl, bx - 2, SPKBASE - h - 2, bw + 4, h + 3);   // halo no "agora"
      g.FillRectangle(B(cc), bx, SPKBASE - h, bw, h);
    }
  }

  // glifo de tendencia do APM (sobe/desce/estavel) vs ~10s atras
  void DrawTrend(Graphics g, float x, float y) {
    int t = apm - apmRef;
    if (t > 0) { var p = new PointF[] { new PointF(x, y + 6), new PointF(x + 8, y + 6), new PointF(x + 4, y) }; g.FillPolygon(B(Online), p); }
    else if (t < 0) { var p = new PointF[] { new PointF(x, y), new PointF(x + 8, y), new PointF(x + 4, y + 6) }; g.FillPolygon(B(Red), p); }
    else { g.FillRectangle(B(Faint), x, y + 3, 8, 2f); }
  }

  string IdleText() {
    if (lastFeedTs <= 0) return "";
    long s = (NowMs() - lastFeedTs) / 1000; if (s < 0) s = 0;
    if (s < 2) return "agora";
    if (s < 60) return "ha " + s + "s";
    long m = s / 60, ss = s % 60;
    return "ha " + m + ":" + ss.ToString("D2");
  }
  Color IdleColor() {
    long s = (NowMs() - lastFeedTs) / 1000;
    if (s < 8) return Online;
    if (s < 30) return AmberMut;
    return Red;
  }

  Color TagColor(string tag) {
    switch (tag) {
      case "EDIT": return Amber;
      case "EXEC": case "TEST": case "AGNT": case "JVS": case "REQ": return AmberBright;
      case "DPLY": case "END": return Red;
      case "GIT": case "DONE": return Online;
      default: return AmberMut;
    }
  }

  // acumula o angulo do reator: 16 graus/s em repouso .. ~62 graus/s a plena carga.
  // Velocidade VARIAVEL sem "saltos" (integra por dt real; nao multiplica a fase por um
  // fator que muda). Chamado 1x por quadro no anim tick.
  void UpdateSpin() {
    long n = NowMs(); if (spinMs == 0) spinMs = n;
    double sp = 16.0 + 46.0 * CoreEnergy();
    spin += (n - spinMs) / 1000.0 * sp; spinMs = n;
    if (spin > 3600000.0) spin -= 3600000.0;            // multiplo de 360 -> mesmo angulo, sem estourar
  }

  // NUCLEO DO JARVIS: aneis girando + RAIOS DE LUZ emanando + glow radial respirando + core.
  // Anima SEMPRE (o animTimer roda continuo e repinta so o atomRect -> custo baixo). REATIVO:
  // gira mais rapido, brilha mais forte e solta DESCARGAS eletricas conforme a atividade real.
  // MODO FABLE 5 (classe Mythos): paleta ouro-branco, 16 raios mais longos, 3 satelites
  // orbitando com rastro e nucleo branco puro -- mesmos primitivos baratos de sempre.
  void DrawCore(Graphics g, float cx, float cy) {
    float R = 30;
    float a = (float)spin;                                    // rotacao base (graus) -- reativa
    double energy = CoreEnergy();                             // 0..1 atividade (carga/APM)
    double rev = 0.72 + 0.5 * energy;                         // "revs" -> brilho sobe com a carga
    double pulse = 0.5 + 0.5 * Math.Sin(phase * (2.2 + 1.6 * energy));   // respiro acelera sob carga
    Color cMain = HC(Amber, MythGold), cHot = HC(AmberBright, MythPale);

    if (heat > 0.02) {   // corona de OVERHEAT: brasa tremulando atras do nucleo (entra com o calor)
      double fl = 0.55 + 0.45 * Math.Sin(phase * 7.3) * Math.Sin(phase * 3.1);
      using (var hb = new SolidBrush(Color.FromArgb((int)((34 + 30 * fl) * heat), Ember))) g.FillEllipse(hb, cx - 30, cy - 30, 60, 60);
    }
    using (var gl = new SolidBrush(Color.FromArgb(Clamp255((int)((36 + 10 * heat) * rev + 26 * energy)), cHot))) g.FillEllipse(gl, cx - 24, cy - 24, 48, 48);
    using (var p = new Pen(Color.FromArgb(55, cMain), 1f)) g.DrawEllipse(p, cx - R, cy - R, 2 * R, 2 * R);
    using (var p = new Pen(Color.FromArgb(22, BorderC), 1f)) g.DrawEllipse(p, cx - R - 3, cy - R - 3, 2 * R + 6, 2 * R + 6);

    // anel de 12 ticks girando
    var st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(a);
    using (var p = new Pen(Color.FromArgb(120, cMain), 2f)) {
      for (int i = 0; i < 12; i++) { double r = i * Math.PI / 6; g.DrawLine(p, (float)(Math.Cos(r) * 25), (float)(Math.Sin(r) * 25), (float)(Math.Cos(r) * 29), (float)(Math.Sin(r) * 29)); }
    }
    g.Restore(st);

    // anel quebrado contra-girando (2 arcos)
    st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(-a * 1.35f);
    using (var p = new Pen(Color.FromArgb(200, cHot), 2.3f)) {
      p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
      g.DrawArc(p, -21, -21, 42, 42, 18, 116); g.DrawArc(p, -21, -21, 42, 42, 198, 116);
    }
    g.Restore(st);

    // anel tracejado externo contra-girando (moldura viva do reator; 1 elipse por frame)
    st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(-a * 0.4f);
    using (var p = new Pen(Color.FromArgb(60, cMain), 1f)) { p.DashPattern = new float[] { 2.6f, 5f }; g.DrawEllipse(p, -33.5f, -33.5f, 67f, 67f); }
    g.Restore(st);

    // motas de energia: 2 particulas orbitando com rastro (vida em repouso; o Fable
    // ja tem os satelites Mythos, entao elas se recolhem no overheat)
    if (heat < 0.5) {
      for (int i = 0; i < 2; i++) {
        double mang = phase * (i == 0 ? 0.9 : -0.7) + i * 2.2;
        double orb = 24 - 5 * Math.Sin(phase * 0.8 + i * 1.9);
        float mx = cx + (float)(Math.Cos(mang) * orb), my = cy + (float)(Math.Sin(mang) * orb);
        using (var tr = new SolidBrush(Color.FromArgb(55, cMain))) g.FillEllipse(tr, mx - 2.4f, my - 2.4f, 4.8f, 4.8f);
        using (var mb = new SolidBrush(Color.FromArgb(190, cHot))) g.FillEllipse(mb, mx - 1.1f, my - 1.1f, 2.2f, 2.2f);
      }
    }

    // RAIOS DE LUZ emanando do centro: giram devagar e respiram (Fable = 16 e mais longos;
    // a contagem troca no meio da transicao, mascarada pelo flash/onda)
    st = g.Save(); g.TranslateTransform(cx, cy); g.RotateTransform(a * 0.55f);
    int rays = heat >= 0.5 ? 16 : 12;
    double lenL = 16.0 + 2.0 * heat, lenS = 9.5 + 1.5 * heat;
    for (int i = 0; i < rays; i++) {
      double ang = i * 2 * Math.PI / rays;
      bool lng = (i % 2 == 0);
      double len = (lng ? lenL : lenS) + pulse * (lng ? 4.5 : 2.5);
      float x1 = (float)(Math.Cos(ang) * 3.2), y1 = (float)(Math.Sin(ang) * 3.2);
      float x2 = (float)(Math.Cos(ang) * len), y2 = (float)(Math.Sin(ang) * len);
      int al = Clamp255((int)((lng ? 205 : 125) * (0.55 + 0.45 * pulse) * rev));
      using (var p = new Pen(Color.FromArgb(al, lng ? cHot : cMain), lng ? 2.1f : 1.4f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, x1, y1, x2, y2); }
    }
    g.Restore(st);

    // satelites Mythos (so no Fable): 3 pontos de ouro-branco orbitando + rastro + orbita tenue
    // (nascem/somem em fade junto com o calor da transicao)
    if (heat > 0.02) {
      using (var p = new Pen(Color.FromArgb((int)(26 * heat), MythGold), 1f)) g.DrawEllipse(p, cx - 35, cy - 35, 70, 70);
      for (int i = 0; i < 3; i++) {
        double ang = -phase * 0.55 + i * 2 * Math.PI / 3;   // orbita mais lenta = movimento liso a 30fps
        float sxp = cx + (float)(Math.Cos(ang) * 35), syp = cy + (float)(Math.Sin(ang) * 35);
        double tg = ang + 0.22;
        using (var tr = new SolidBrush(Color.FromArgb((int)(80 * heat), MythGold))) g.FillEllipse(tr, cx + (float)(Math.Cos(tg) * 35) - 1.3f, cy + (float)(Math.Sin(tg) * 35) - 1.3f, 2.6f, 2.6f);
        using (var sb = new SolidBrush(Color.FromArgb((int)(235 * heat), MythPale))) g.FillEllipse(sb, sxp - 2f, syp - 2f, 4f, 4f);
      }
    }

    // DESCARGA ELETRICA: sob carga, um arco crepita entre o nucleo e o anel externo.
    // Periodo encurta com a atividade (calmo em repouso -> tempestade a plena carga).
    if (energy > 0.12) {
      double dperiod = 3200.0 - 2100.0 * energy;            // ms entre descargas
      long dph = NowMs() % (long)dperiod;
      if (dph < 200) DrawDischarge(g, cx, cy, 1.0 - dph / 200.0);
    }

    // glow radial "emanando" (respira em raio e brilho; Fable = mais branco e intenso; sobe c/ carga)
    float gr = 13f + (float)(pulse * 4.5) + (float)(3.0 * energy);
    using (var gpath = new GraphicsPath()) {
      gpath.AddEllipse(cx - gr, cy - gr, 2 * gr, 2 * gr);
      using (var pgb = new PathGradientBrush(gpath)) {
        pgb.CenterPoint = new PointF(cx, cy);
        pgb.CenterColor = Color.FromArgb(Clamp255((int)(150 + 75 * pulse + 15 * heat + 30 * energy)), 255, (int)(240 + 8 * heat), (int)(205 + 23 * heat));
        pgb.SurroundColors = new Color[] { Color.FromArgb(0, HC(Amber, Ember)) };
        g.FillPath(pgb, gpath);
      }
    }

    // nucleo incandescente + branco quente (Fable = maior, com miolo branco puro; incha c/ carga)
    float pr = (float)(4.6 + 0.6 * heat + 0.9 * energy) + (float)(pulse * (1.7 + 0.4 * heat));
    using (var b = new SolidBrush(cMain)) g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);
    using (var b = new SolidBrush(Lerp(Color.FromArgb(255, 255, 248, 232), Color.White, heat))) g.FillEllipse(b, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);
  }

  // arco eletrico crepitante do nucleo ate o anel externo: polilinha jagged com halo,
  // angulo/jitter pseudo-aleatorios (semente = tempo) e alfa decaindo. Dentro do atomRect.
  void DrawDischarge(Graphics g, float cx, float cy, double k) {
    long seed = NowMs() / 380;                               // muda a cada descarga
    double a0 = (seed * 2.3999) % (2 * Math.PI);
    int segs = 5; var pts = new PointF[segs + 1];
    for (int i = 0; i <= segs; i++) {
      double f = i / (double)segs;
      double ang = a0 + Math.Sin(seed * 1.7 + i * 0.9) * 0.5;
      double rad = 5 + 24 * f;
      double jit = (i == 0 || i == segs) ? 0 : Math.Sin(seed * 3.1 + i * 2.7) * 5.0;
      pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad + Math.Cos(ang + 1.57) * jit),
                          cy + (float)(Math.Sin(ang) * rad + Math.Sin(ang + 1.57) * jit));
    }
    Color hotc = heat >= 0.5 ? MythPale : Color.FromArgb(255, 255, 244, 214);
    using (var halo = new Pen(Color.FromArgb((int)(80 * k), heat >= 0.5 ? MythGold : AmberBright), 3.4f)) { halo.StartCap = LineCap.Round; halo.EndCap = LineCap.Round; halo.LineJoin = LineJoin.Round; g.DrawLines(halo, pts); }
    using (var bolt = new Pen(Color.FromArgb((int)(230 * k), hotc), 1.4f)) { bolt.StartCap = LineCap.Round; bolt.EndCap = LineCap.Round; bolt.LineJoin = LineJoin.Round; g.DrawLines(bolt, pts); }
  }

  static int Clamp255(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

  // badge "FABLE 5" no cabecalho: estrela de 4 pontas desenhada (sem depender de glifo)
  // + texto ouro-branco, brilho respirando em passos de 1s (fora do atomRect -> pintado
  // apenas no Invalidate cheio de 1Hz, custo desprezivel).
  void DrawFableBadge(Graphics g, float x, float y) {
    double k = 0.5 + 0.5 * Math.Sin(NowMs() / 650.0);
    string txt = "FABLE 5";
    var sz = g.MeasureString(txt, fChip);
    float w = sz.Width + 20, h = 15;
    using (var gp = RoundedPath(x, y, w, h, 4)) {
      using (var fill = new SolidBrush(Color.FromArgb((int)((26 + 30 * k) * heat), MythGold))) g.FillPath(fill, gp);
      using (var pen = new Pen(Color.FromArgb((int)((150 + 90 * k) * heat), MythGold), 1f)) g.DrawPath(pen, gp);
    }
    float sx = x + 9, sy = y + h / 2f;
    var pts = new PointF[8];
    for (int i = 0; i < 8; i++) {
      double ang = -Math.PI / 2 + i * Math.PI / 4;
      float rr = (i % 2 == 0) ? 4.4f : 1.5f;
      pts[i] = new PointF(sx + (float)(Math.Cos(ang) * rr), sy + (float)(Math.Sin(ang) * rr));
    }
    g.FillPolygon(B(Color.FromArgb((int)((190 + 65 * k) * heat), MythPale)), pts);
    g.DrawString(txt, fChip, B(Color.FromArgb((int)((200 + 55 * k) * heat), MythPale)), x + 15, y + 3.4f);
  }

  string Elapsed() {
    if (startTs <= 0) return "00:00";
    long s = (NowMs() - startTs) / 1000; if (s < 0) s = 0;
    long h = s / 3600, m = (s % 3600) / 60, ss = s % 60;
    return h > 0 ? string.Format("{0}:{1:D2}:{2:D2}", h, m, ss) : string.Format("{0:D2}:{1:D2}", m, ss);
  }
  string Fit(Graphics g, string s, Font f, float max) {
    if (g.MeasureString(s, f).Width <= max) return s;
    while (s.Length > 1 && g.MeasureString(s + "...", f).Width > max) s = s.Substring(0, s.Length - 1);
    return s + "...";
  }

  // popula dados sinteticos p/ o modo --shot (QA visual)
  public void Seed() {
    title = "Claude Code";
    startTs = NowMs() - (1000L * 60 * 7) - (1000L * 23);   // 07:23 de operacao
    status = "OPERANDO"; phase = 1.15; spin = 41; modelShort = "OPUS"; loadPeak = 0.86;
    long now = NowMs();
    int[] off = { 1, 3, 4, 7, 9, 12, 15, 19, 24, 30, 38, 47, 58 };
    for (int i = 0; i < off.Length; i++) { actTs[(int)(actions % actTs.Length)] = now - off[i] * 1000L; actions++; }
    lastFeedTs = now - 3000; lastJvsTs = now - 1100;
    taskDone = 0; taskTotal = 0;   // mostra a CARGA do reator (sem checklist)
    feed.Add(new string[] { (now - 12000).ToString(), "REQ", "Deixar o HUD do Jarvis melhor" });
    feed.Add(new string[] { (now - 9000).ToString(), "READ", "Lendo JarvisHudWF.cs" });
    feed.Add(new string[] { (now - 6500).ToString(), "EDIT", "Editando JarvisHudWF.cs" });
    feed.Add(new string[] { (now - 4200).ToString(), "EXEC", "Executando: csc build" });
    feed.Add(new string[] { (now - 1100).ToString(), "JVS", "Renderizando com as especificacoes, senhor." });
    Recompute();
  }

  // ------- interacao -------
  protected override void OnMouseDown(MouseEventArgs e) {
    if (e.Button == MouseButtons.Left) {
      if (morphing || booting || closing || hideFly != 0) return;   // durante morph/ignicao/desligamento/succao, ignora cliques
      if (minimized) {
        if (miniCloseRect.Contains(e.Location)) { try { File.WriteAllText(Path.Combine(dir, "closed"), "1"); } catch {} Close(); return; }
        dragging = true; dragStart = e.Location;              // clique simples restaura (decidido no MouseUp)
        base.OnMouseDown(e); return;
      }
      if (closeRect.Contains(e.Location)) { try { File.WriteAllText(Path.Combine(dir, "closed"), "1"); } catch {} Close(); return; }
      if (minRect.Contains(e.Location)) { BeginMorph(true); return; }   // minimizar
      dragging = true; dragStart = e.Location;
    }
    base.OnMouseDown(e);
  }
  protected override void OnMouseMove(MouseEventArgs e) {
    if (dragging) {
      int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
      // o Windows dispara WM_MOUSEMOVE espurio (delta ZERO) logo apos o mouse-down;
      // sem este guard, um CLIQUE parado virava "arrasto de 0px" -> Release do dock
      // em vez de restaurar a capsula (bug pego em 20/07)
      if (dx != 0 || dy != 0) { Location = new Point(Location.X + dx, Location.Y + dy); movedDuringDrag = true; }
    }
    base.OnMouseMove(e);
  }
  protected override void OnMouseUp(MouseEventArgs e) {
    if (dragging) {
      dragging = false;
      if (movedDuringDrag) { userMoved = true; HudLayout.Release(pid); }
      else if (minimized && !morphing) { BeginMorph(false); }   // clique simples na mini -> restaura
      movedDuringDrag = false;
    }
    base.OnMouseUp(e);
  }
  protected override void OnFormClosed(FormClosedEventArgs e) {
    try { if (periodSet) { timeEndPeriod(1); periodSet = false; } } catch {}   // devolve a resolucao do timer
    try { if (bgBmp != null) { bgBmp.Dispose(); bgBmp = null; } } catch {}
    try { if (miniBg != null) { miniBg.Dispose(); miniBg = null; } } catch {}
    try { if (fullShot != null) { fullShot.Dispose(); fullShot = null; } } catch {}
    try { if (miniShotBmp != null) { miniShotBmp.Dispose(); miniShotBmp = null; } } catch {}
    try { HudLayout.Release(pid); } catch {}
    try { if (mutex != null) mutex.ReleaseMutex(); } catch {}
    base.OnFormClosed(e);
  }
}

// ---- acabamento HOLOGRAFICO compartilhado (telinha de sessao + fan-out) ----
// "Vidro" do painel: scanlines finas + vinheta radial. Gerado UMA unica vez por
// tamanho e cacheado; desenhar custa 1 DrawImage por repintura cheia (1Hz).
static class Cine {
  static Dictionary<long, Bitmap> cache = new Dictionary<long, Bitmap>();
  public static Bitmap Overlay(int w, int h) {
    long k = ((long)w << 16) | (uint)h;
    if (cache.ContainsKey(k)) return cache[k];
    var bmp = new Bitmap(w, h);
    using (var g = Graphics.FromImage(bmp)) {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      using (var ln = new SolidBrush(Color.FromArgb(13, 0, 0, 0)))          // scanlines sutis
        for (int y = 1; y < h; y += 3) g.FillRectangle(ln, 0, y, w, 1);
      using (var gp = new GraphicsPath()) {                                  // vinheta: centro limpo, cantos escurecem
        gp.AddEllipse(-w * 0.35f, -h * 0.45f, w * 1.7f, h * 1.9f);
        using (var pgb = new PathGradientBrush(gp)) {
          pgb.CenterPoint = new PointF(w / 2f, h * 0.42f);
          pgb.CenterColor = Color.FromArgb(0, 0, 0, 0);
          pgb.SurroundColors = new Color[] { Color.FromArgb(66, 0, 0, 0) };
          g.FillPath(pgb, gp);
        }
      }
    }
    cache[k] = bmp; return bmp;
  }
}
