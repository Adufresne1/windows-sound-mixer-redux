# Volume Mixer

Un mélangeur de volume Windows repensé — simple, lisible, Fluent (Mica, thème clair/sombre, accent système). Contrôle directement Windows Core Audio (WASAPI) ; aucun réglage son n'est persisté : **Windows reste la source de vérité**.

[![Télécharger](https://img.shields.io/github/v/release/Adufresne1/windows-sound-mixer-redux?include_prereleases&label=t%C3%A9l%C3%A9charger&color=2ea44f&sort=semver)](https://github.com/Adufresne1/windows-sound-mixer-redux/releases/latest)

## ⬇️ Télécharger (Windows x64)

**[➡️ Dernière version (page des releases)](https://github.com/Adufresne1/windows-sound-mixer-redux/releases)**

### Installation (recommandé)

1. Télécharge le fichier `SoundMixerRedux-*-win-x64-Setup.msi`.
2. Double-clique dessus et suis l'installeur.
3. Lance **Sound Mixer Redux** depuis le menu Démarrer.

### Version portable (sans installation)

1. Télécharge le fichier `SoundMixerRedux-*-win-x64.zip`.
2. Extrais-le **où tu veux** (Bureau, Documents…).
3. Ouvre le dossier `SoundMixerRedux` et double-clique **`SoundMixerRedux.exe`**.

> **Aucune installation requise** pour la version portable — .NET 8 et le Windows App SDK sont inclus dans le zip.
> ⚠️ Windows SmartScreen peut avertir (installeur/exe non signés) : *Informations complémentaires → Exécuter quand même*.

## Fonctionnalités

- Master **Sortie** et **Entrée** + **volume / muet par application** (sessions regroupées par app, comme le mélangeur Windows), synchro temps réel dans les deux sens.
- **Vumètres dBFS** post-fader, échelle graduée (activable/désactivable).
- **Solo** : coupe réellement les autres canaux, mémorise puis restaure leur état, transfert A/B.
- **Change le périphérique par défaut Windows** depuis les sélecteurs + détection à chaud (branchement/débranchement).
- Réglages persistés : toujours au premier plan, échelle dB, position/taille de la fenêtre (multi-écrans).

## Compiler depuis les sources

Prérequis : **.NET 8 SDK** (charge de travail Windows).

```bash
dotnet build SoundMixerRedux/SoundMixerRedux.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
```

Le build autonome est produit dans
`SoundMixerRedux/bin/x64/Release/net8.0-windows10.0.26100.0/win-x64/` — copie tout ce dossier pour le distribuer.

## Statut

Pré-version — phases 0→7 terminées (audio réel, sessions par app, vumètres, solo, périphériques live, réglages persistés). À venir : localisation (fr/en), accessibilité, polish.

---

_WinUI 3 · .NET 8 · MVVM (CommunityToolkit) · NAudio/WASAPI · application non empaquetée (unpackaged)._
