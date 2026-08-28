# Volume Mixer — V1 → V2

## Résumé de la V1 actuelle

**Stack** : WinUI 3 / Windows App SDK 2.3.1, C# .NET 8, MVVM (CommunityToolkit.Mvvm), NAudio.Wasapi. Application **unpackaged self-contained** (pas de MSIX). Aucun réglage son n'est persisté par l'app — Windows (Core Audio) reste la seule source de vérité, lue au démarrage et reflétée en temps réel via callbacks.

**Fonctionnalités livrées** (phases 0 à G, toutes terminées ou explicitement annulées) :
- Contrôle **Master Sortie/Entrée** + **volume/mute par application** (sessions regroupées par process, comme le mixer natif Windows), synchro bidirectionnelle temps réel.
- **Vumètres dBFS post-fader** avec échelle graduée togglable, zones ancrées via masque calculé (pas de Viewbox).
- **Solo par section** : mute réel des autres canaux (sauf Master), mémorisation/restauration des mutes antérieurs, transfert A/B.
- **Changement du périphérique par défaut Windows** depuis les sélecteurs (`IPolicyConfig` non documenté) + détection à chaud (branchement/débranchement, `IMMNotificationClient`).
- **Réglages persistés** : toujours au premier plan, échelle dB, position **et** taille de fenêtre (restauration multi-écrans robuste).
- **Fenêtre sans chrome natif** : plus de barre de titre Windows, déplacement par clic-glisser n'importe où sur la fenêtre sauf sur un contrôle interactif (slider, bouton, combobox — via `InputNonClientPointerSource.SetRegionRects(Passthrough, ...)`, recalculé au layout/scroll), fermeture déplacée dans le menu Settings, plus de minimize/maximize.
- **Contenu qui scale avec la fenêtre** : resize manuel normal (drag utilisateur), le contenu (`ChannelStrip`) rescale via un facteur `BoardScale` recalculé itérativement pour remplir la taille client courante (pas de resize de fenêtre au contenu).
- **Show/hide de piste** : mode de sélection transitoire (bouton Settings → Done), badge œil rouge sur les pistes masquées, indicateur agrégé vert/rouge près de Settings selon qu'une piste masquée joue du son.
- **Localisation FR/EN** automatique selon la langue d'affichage Windows (`.resw` + `x:Uid`, repli anglais).

**Annulé en V1** (décision explicite, pas un oubli) :
- Options Settings « Stick to right » et « Pin » (ancrage/verrouillage de position de fenêtre).
- Investigation sessions "fantômes" de composants pilote GPU (AMDRSServ et équivalents) — le show/hide de piste sert de mitigation si le problème se représente.

**Statut distribution** : build autonome (`dotnet build -c Release -r win-x64 --self-contained`), release GitHub avec zip téléchargeable. Pas encore packagé comme app de release "officielle" (icônes custom pas encore intégrées, `Package.appxmanifest` a toujours le `Publisher="CN=AppPublisher"` placeholder — non utilisé en unpackaged mais à traiter si on package un jour).

## Notes techniques pour la V2

### 1. Son "ding" au clic sur le fader Système (comme le son natif Windows)

Windows joue un chime (`.wav`) quand on ajuste le volume via l'OSD/les touches clavier — à identifier précisément avant d'implémenter (probablement un asset sous `%WINDIR%\Media\`, ou un accès via une API système type `PlaySound`/`SystemSounds` — **pas encore confirmé, à vérifier en V2**).

Pistes d'implémentation :
- Se déclencher uniquement sur le fader **Système** (canal Master Sortie), pas sur les faders d'app.
- Se déclencher sur le **relâchement** du slider (`PointerReleased`/fin de drag), pas à chaque tick de `Value` — sinon spam audio pendant qu'on glisse. `Slider` WinUI n'a pas d'event "release" direct ; il faudra probablement écouter `PointerCaptureLost`/`PointerReleased` au niveau du `Slider` plutôt que `ValueChanged`.
- Si le fichier exact de Windows n'est pas réutilisable/accessible proprement, embarquer un asset `.wav` équivalent dans le projet et le jouer via `MediaPlayer` (Windows.Media.Playback) ou `System.Media.SoundPlayer`.

### 2. Framework de resizing/layout plus flexible

Mécanisme actuel (Phase A) : chaque dimension fixe de `ChannelStrip.xaml` (largeur carte, tuile, polices, hauteur fader, meter, slider, boutons — une vingtaine de valeurs) est bindée individuellement via un `ScaleConverter` (`valeur × BoardScale`). `BoardScale` est recalculé dans `MainWindow.xaml.cs` (`RecomputeScale`) par mesure itérative (measure-and-correct, 1-4 passes) pour remplir la taille client de la fenêtre. **Volontairement pas de `Viewbox`/`RenderTransform`** : essayé et retiré en Phase 4 à cause d'un bug de positionnement des `Popup` (tooltips/flyouts/dropdowns) sous un ancêtre transformé, en WinUI/UWP.

Pistes à explorer en V2 :
- Vérifier si une version plus récente du Windows App SDK a corrigé le bug de positionnement des `Popup` sous transformation — si oui, un `Viewbox`/`RenderTransform` redeviendrait une option nettement plus simple que ~20 bindings individuels par propriété.
- Sinon, explorer un layout responsive plus natif à WinUI (`VisualStateManager` + `AdaptiveTrigger` par paliers de taille, `RelativePanel`) pour réduire le nombre de valeurs à scaler manuellement, quitte à perdre le scaling continu au profit de paliers discrets.
- Dans tous les cas, garder le contrainte actuelle : les `ToolTipService.ToolTip` (Muet/Solo) et les `Flyout`/`MenuFlyout` (Settings) doivent rester correctement positionnés à n'importe quelle taille de fenêtre.

### 3. Bordures plus petites / retirer la barre grise autour de la fenêtre

Deux sources possibles à distinguer avant de coder (idéalement avec une capture d'écran annotée par l'utilisateur) :
- **Bordure de carte** : chaque `ChannelStrip` a `BorderThickness="1"` + `CardStrokeColorDefaultBrush` (`Controls/ChannelStrip.xaml`) — réduction ou suppression triviale si c'est ça qui est visé.
- **Cadre natif de la fenêtre** : même avec `ExtendsContentIntoTitleBar` + chrome custom (Phase D) + `MicaBackdrop`, WinUI/DWM dessine encore le cadre de resize standard (`OverlappedPresenter`) autour d'une fenêtre redimensionnable — le retirer entièrement demanderait de sortir du modèle de présentateur standard (style de fenêtre différent, ex. `WS_POPUP`), ce qui remettrait en jeu le resize par bord et les comportements d'accrochage (Aero Snap) déjà acquis. Changement plus lourd que le point précédent — à confirmer que c'est bien ce qui est visé avant de s'engager dessus.

### 4. Revoir l'UI de la top bar (Settings)

État actuel : dans chaque section (Sortie/Entrée), un en-tête `Grid` à 3 colonnes combine le `DropDownButton` Settings (ou bouton Done en mode sélection), l'indicateur œil agrégé, le label de section, et le `ComboBox` de périphérique — déjà noté comme dense (mémoire projet : la seule ligne d'en-tête consomme ~730px, ce qui a limité une feature annexe de cap automatique de fenêtre). Pas de direction concrète encore définie pour la refonte — nécessite une maquette ou des préférences précises de l'utilisateur avant implémentation (disposition, quels éléments rester visibles en permanence vs dans un menu, etc.).

### 5. Perf : throttle des vumètres + coalescer les callbacks COM

Repris de l'ancienne Phase 9 (non-fonctionnel), jamais traité.

- **Timer VU** (`MixerViewModel.cs:119`) : `DispatcherTimer` à 33ms (~30Hz), fait un appel COM (`Peak`) par canal par tick, **même fenêtre minimisée/non visible** — pas de pause hors focus/visibilité. Candidat le plus concret pour un throttle réel.
- **`UpdatePassthroughRegions()`** (`MainWindow.xaml.cs:298`) : parcours récursif complet de l'arbre visuel + réallocation d'une `List<RectInt32>` à **chaque** `LayoutUpdated` — un event WinUI connu pour se déclencher plus souvent que les vrais changements de layout. Candidat de coalescing/debounce si un ralentissement est mesuré au profiling.

### 6. Accessibilité

Repris de l'ancienne Phase 9, rien fait à ce jour : navigation clavier, focus visible, support Narrator/UI Automation, reduced-motion.

### 7. Robustesse

Repris de l'ancienne Phase 9, rien fait à ce jour : débranchement de périphérique en cours d'usage, perte de session audio, états vides propres.

### 8. Fermer vers la zone de notification (tray) + icône systray

- **Option Settings "Fermer vers la zone de notification"** : au lieu de quitter le process, le bouton Fermer masquerait la fenêtre. Techniquement, `Window.Closed` (câblé dans `MainWindow.xaml.cs`) est déjà un événement de fermeture actée, pas annulable — il faudra intercepter via `AppWindow.Closing` (annulable, `args.Cancel`) et masquer (`AppWindow.Hide()`) plutôt que fermer quand l'option est active.
- **Icône systray + menu contextuel** : à créer de zéro, WinUI 3 n'a pas de composant natif pour ça. Candidats non tranchés : P/Invoke direct sur `Shell_NotifyIcon` (Win32), ou une lib tierce (ex. `H.NotifyIcon.WinUI`) — vérifier compatibilité avec le mode unpackaged self-contained actuel avant de choisir. Fonctionnalités à définir : clic (simple/double ?) pour restaurer/masquer la fenêtre, menu contextuel avec au minimum "Ouvrir" et "Quitter" (vraie fermeture, pour sortir du mode tray).
