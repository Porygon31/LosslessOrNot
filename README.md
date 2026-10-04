# LosslessOrNot

**English** · [Français](#francais)

<a id="english"></a>

## English

LosslessOrNot is a Windows desktop app that tells whether audio files are really
lossless — that is, it spots "fake lossless" files: FLAC, ALAC or WAV files made from
an MP3 or AAC source.

> [!WARNING]
> **Work in progress.** LosslessOrNot is still under development and this is only a
> first draft. Its verdicts come from a heuristic and can be wrong, and the interface
> and code will keep changing. Do not rely on it alone to delete or replace files.

### How it works

1. **ffprobe** reads what the file claims to be: codec, sample rate, bit depth.
2. **ffmpeg** decodes an excerpt to mono, at the file's own sample rate, and the
   analysis reads it as it arrives. The excerpt lasts 45 s by default — adjustable
   from 5 to 600 s in the toolbar — and starts a quarter of the way in (at most 60 s
   in, at the start for tracks shorter than 20 s, and early enough to fit in the file).
   A file shorter than the chosen duration is analysed whole, and the result says so.
3. A 4096-point FFT (Hann window, 50 % overlap) is averaged over every non-silent
   frame of the excerpt.
4. The analysis measures where the content ends and looks for the steepest 1 kHz drop.
   A lossy encoder cuts the treble with a low-pass "wall" and zeroes everything above
   it, so the verdict is "lossy" only when three signals agree:
   - the level drops by at least 20 dB within 1 kHz;
   - the wall sits at or below 21 kHz;
   - the level above it falls to −88 dB or lower.

   Content above 20.5 kHz, out of reach of lossy encoders, is consistent with a true
   lossless master. Anything else is reported as inconclusive rather than guessed.
   The thresholds are absolute frequencies, not fractions of Nyquist, so that genuine
   hi-res files with nothing above ~24 kHz are not condemned.
5. The spectral verdict is combined with the codec the file claims to give its status.

### Statuses

The interface is in French; the statuses below are shown as they appear in the app.

| Status | Meaning |
| --- | --- |
| Lossless authentique / Lossless Hi-Res | Lossless codec with content beyond 20.5 kHz. "Hi-Res" when the file is at least 24-bit / 88.2 kHz. |
| Faux lossless | Lossless codec, but the spectrum carries a lossy encoder's wall (confidence ≥ 75 %): probably a transcode. The likely source is named, e.g. "MP3 ≈320 kbps". |
| Suspect | The spectrum looks lossy, but too little audio could be analysed to assert it. |
| Non concluant | The spectrum cannot settle the question: little treble, no clear codec wall. |
| Format avec perte | The codec itself is lossy (MP3, AAC, Opus, Vorbis, WMA…): not lossless, whatever the spectrum shows. |
| Erreur | The file could not be read or analysed. |

### Requirements

- Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- **ffmpeg** and **ffprobe**. They are looked up in the folder chosen with the
  "ffmpeg…" button, then in the application folder (and its `ffmpeg\` or
  `ffmpeg\bin\` subfolders), then on the `PATH`. The chosen folder is remembered in
  `%APPDATA%\LosslessOrNot\settings.json`, along with the analysis duration, the
  spectrogram palette and the theme.

### Usage

Run the app:

```shell
dotnet run --project LosslessOrNot
```

- Add files or folders with the buttons, by drag and drop, or on the command line
  (`dotnet run --project LosslessOrNot -- "D:\Music"`). Folders are scanned
  recursively.
- Recognised extensions: `.flac`, `.wav`, `.aif`, `.aiff`, `.aifc`, `.m4a`, `.alac`,
  `.ape`, `.wv`, `.tta`, `.mp3`, `.aac`, `.ogg`, `.oga`, `.opus`, `.wma`.
- Choose how many seconds of each file are analysed with **Durée analysée** in the
  toolbar. It applies to the next analyses; right-click › Réanalyser updates files
  already analysed.
- Select a file to see its measurements and the evidence behind the verdict, its
  spectrogram (wheel to zoom, drag to pan, double-click to reset, hover to read the
  values) and its averaged spectrum with the decision thresholds.
- The **Explications** tab tells why the file got its status, criterion by criterion:
  what each measurement means, the value found, the threshold it is held to, and
  whether it points to a lossy encoder (✘), reassures (✔), informs (ℹ) or limits the
  analysis (⚠).
- **Thème** switches between the classic and the dark theme. The change applies after
  a restart, which the app offers to do.

Run the tests (those that need ffmpeg are skipped when it cannot be found):

```shell
dotnet test
```

### Project structure

- `LosslessOrNot.Core` — the analysis engine, without any UI: FFT, verdict, calls to
  ffmpeg and ffprobe, final status.
- `LosslessOrNot` — the WinForms application.
- `LosslessOrNot.Tests` — xUnit tests, from pure FFT checks to end-to-end runs on files
  generated with ffmpeg.

### Known limitations and next steps

- Fake hi-res is not detected: a 44.1 kHz master up-sampled to 24-bit / 96 kHz is
  shown as "Lossless Hi-Res".
- Fake 24-bit (16-bit audio padded to 24 bits) is not detected.
- Only one excerpt is analysed (45 s by default), downmixed to mono.
- No CSV or PNG export yet.
- The interface is only available in French.
- The dark theme needs Windows 11 (and no high-contrast mode); message boxes stay
  light whatever the theme.

### Origin and license

The spectral analysis is a C# port of the one in
[Antra](https://github.com/anandprtp/Antra) (`spectrum.go`), which is licensed under
the Elastic License 2.0. The derived files and the terms of that license are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). No license has been chosen yet for
the rest of the code.

---

<a id="francais"></a>

## Français

[English](#english) · **Français**

LosslessOrNot est une application de bureau Windows qui indique si des fichiers audio
sont vraiment lossless. Elle repère les « faux lossless » : des fichiers FLAC, ALAC ou
WAV fabriqués à partir d'une source MP3 ou AAC.

> [!WARNING]
> **Projet en cours de développement.** LosslessOrNot n'en est qu'à une première
> ébauche. Ses verdicts reposent sur une heuristique et peuvent se tromper, et
> l'interface comme le code vont encore évoluer. Ne vous en servez pas comme seule
> preuve pour supprimer ou remplacer des fichiers.

### Principe

1. **ffprobe** lit ce que le fichier annonce : codec, fréquence d'échantillonnage,
   profondeur de bits.
2. **ffmpeg** décode un extrait en mono, à la fréquence d'échantillonnage du fichier,
   et l'analyse le lit au fil du décodage. L'extrait dure 45 s par défaut — réglable de
   5 à 600 s dans la barre d'outils — et commence au quart du morceau (au plus tard à
   60 s, dès le début pour les morceaux de moins de 20 s, et assez tôt pour tenir dans
   le fichier). Un fichier plus court que la durée choisie est analysé en entier, et
   le résultat le signale.
3. Une FFT de 4096 points (fenêtre de Hann, recouvrement de 50 %) est moyennée sur
   toutes les trames non silencieuses de l'extrait.
4. L'analyse mesure où s'arrête le contenu et cherche la chute la plus raide sur 1 kHz.
   Un encodeur avec perte coupe les aigus par un « mur » passe-bas et met à zéro tout
   ce qui se trouve au-dessus ; le verdict « avec perte » n'est donc rendu que si trois
   signes concordent :
   - le niveau chute d'au moins 20 dB en 1 kHz ;
   - le mur se situe à 21 kHz ou en dessous ;
   - au-dessus, le niveau retombe à −88 dB ou moins.

   Un contenu qui dépasse 20,5 kHz, hors de portée des encodeurs avec perte, est
   cohérent avec un vrai master lossless. Dans les autres cas, l'analyse se déclare non
   concluante au lieu de deviner. Les seuils sont des fréquences absolues et non des
   fractions de la fréquence de Nyquist, pour ne pas condamner les vrais fichiers
   hi-res, qui n'ont rien au-delà de ~24 kHz.
5. Le verdict spectral est croisé avec le codec annoncé pour donner le statut du
   fichier.

### Statuts

| Statut | Signification |
| --- | --- |
| Lossless authentique / Lossless Hi-Res | Codec lossless, avec du contenu au-delà de 20,5 kHz. « Hi-Res » si le fichier fait au moins 24 bits / 88,2 kHz. |
| Faux lossless | Codec lossless, mais le spectre porte le mur d'un encodeur avec perte (confiance ≥ 75 %) : probablement un transcodage. La source probable est indiquée, par exemple « MP3 ≈320 kbps ». |
| Suspect | Le spectre ressemble à celui d'un encodeur avec perte, mais trop peu d'audio a pu être analysé pour l'affirmer. |
| Non concluant | Le spectre ne permet pas de trancher : peu d'aigus, pas de mur net. |
| Format avec perte | Le codec lui-même est avec perte (MP3, AAC, Opus, Vorbis, WMA…) : le fichier n'est pas lossless, quel que soit son spectre. |
| Erreur | Le fichier n'a pas pu être lu ou analysé. |

### Prérequis

- Windows et le [SDK .NET 10](https://dotnet.microsoft.com/download).
- **ffmpeg** et **ffprobe**. Ils sont cherchés dans le dossier choisi avec le bouton
  « ffmpeg… », puis dans le dossier de l'application (et ses sous-dossiers `ffmpeg\`
  ou `ffmpeg\bin\`), puis dans le `PATH`. Le dossier choisi est mémorisé dans
  `%APPDATA%\LosslessOrNot\settings.json`, avec la durée d'analyse, la palette du
  spectrogramme et le thème.

### Utilisation

Lancer l'application :

```shell
dotnet run --project LosslessOrNot
```

- Ajoutez des fichiers ou des dossiers avec les boutons, par glisser-déposer ou en
  ligne de commande (`dotnet run --project LosslessOrNot -- "D:\Musique"`). Les
  dossiers sont parcourus avec leurs sous-dossiers.
- Extensions reconnues : `.flac`, `.wav`, `.aif`, `.aiff`, `.aifc`, `.m4a`, `.alac`,
  `.ape`, `.wv`, `.tta`, `.mp3`, `.aac`, `.ogg`, `.oga`, `.opus`, `.wma`.
- Choisissez combien de secondes de chaque fichier sont analysées avec **Durée
  analysée** dans la barre d'outils. Le réglage vaut pour les analyses suivantes ;
  clic droit › Réanalyser met à jour les fichiers déjà analysés.
- Sélectionnez un fichier pour voir ses mesures et les preuves du verdict, son
  spectrogramme (molette pour zoomer, glisser pour se déplacer, double-clic pour
  revenir à la vue complète, survol pour lire les valeurs) et son spectre moyen avec
  les seuils de décision.
- L'onglet **Explications** dit pourquoi le fichier a reçu son statut, critère par
  critère : ce que mesure chaque point, la valeur trouvée, le seuil appliqué, et s'il
  évoque un encodeur avec perte (✘), s'il est rassurant (✔), informatif (ℹ) ou s'il
  limite l'analyse (⚠).
- **Thème** permet de passer du thème classique au thème sombre. Le changement
  s'applique après un redémarrage, que l'application propose de faire.

Lancer les tests (ceux qui ont besoin de ffmpeg sont ignorés s'il est introuvable) :

```shell
dotnet test
```

### Structure du projet

- `LosslessOrNot.Core` — le moteur d'analyse, sans interface : FFT, verdict, appels à
  ffmpeg et ffprobe, statut final.
- `LosslessOrNot` — l'application WinForms.
- `LosslessOrNot.Tests` — les tests xUnit, des vérifications de la FFT jusqu'aux tests
  de bout en bout sur des fichiers générés avec ffmpeg.

### Limites connues et prochaines étapes

- Le faux hi-res n'est pas détecté : un master 44,1 kHz suréchantillonné en
  24 bits / 96 kHz apparaît comme « Lossless Hi-Res ».
- Le faux 24 bits (de l'audio 16 bits complété jusqu'à 24 bits) n'est pas détecté.
- Un seul extrait est analysé (45 s par défaut), mixé en mono.
- Pas encore d'export CSV ou PNG.
- Le thème sombre demande Windows 11 (sans mode contraste élevé) ; les boîtes de
  message restent claires quel que soit le thème.

### Origine et licence

L'analyse spectrale est un portage en C# de celle d'[Antra](https://github.com/anandprtp/Antra)
(`spectrum.go`), publiée sous Elastic License 2.0. Les fichiers dérivés et les termes de
cette licence figurent dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Aucune
licence n'a encore été choisie pour le reste du code.
