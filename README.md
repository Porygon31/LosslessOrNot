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
   A lossy encoder cuts the treble with a low-pass "wall" and removes everything above
   it, so the verdict is "lossy" only when three signals agree:
   - the level drops by at least 20 dB within 1 kHz — 15 dB when it lands on a flat
     floor;
   - the wall sits at or below 21 kHz;
   - nothing musical is left above it: the level falls to −88 dB or lower, or to the
     file's own rounding noise (16 or 24 bits), or to a constant noise that ignores the
     music (dither added when decoding), or to a faint image of the music more than
     65 dB below it (what a decoder's filter bank lets through), or to a flat
     distortion (the lossy decode of a loud master overshoots full scale and clips) —
     but never to a residue that closely follows the music while still falling, as a
     natural roll-off leaves.

   Frame by frame, the analysis also checks that the wall holds even in the passages
   richest in treble, and whether the content beyond 21 kHz follows the music: content
   there is out of reach of lossy encoders and points to a true lossless master, while
   dither or hiss proves nothing. Anything else is reported as inconclusive rather
   than guessed. The thresholds are absolute frequencies, not fractions of Nyquist, so
   that genuine hi-res files with nothing above ~24 kHz are not condemned.
5. Every indicator adds its weight, in log-odds, to a probability that the file went
   through a lossy encoder, as a naive Bayes classifier would. The confidence is that
   probability for a lossy verdict, and its complement for a lossless one — at most
   90 %, since AAC at 256 kbps, LAME V0 or Vorbis q6 and above often keep the full
   band and leave no trace. Between 19.1 and 21 kHz, the wall of a high-bitrate
   encoder (MP3 320 kbps, V0, AAC, Opus, Vorbis) and that of a sample-rate converter
   (a master made at 48 kHz and delivered at 44.1 kHz) cannot be told apart: there
   the probability is capped at 50–55 %, and the file is at best "Suspect". These
   rules come from measurements (see *Calibrating the weights*).
6. The spectral verdict is combined with the codec the file claims to give its status.

### Statuses

The interface is in French; the statuses below are shown as they appear in the app.

| Status | Meaning |
| --- | --- |
| Lossless authentique / Lossless Hi-Res | Lossless codec with content beyond 20.5 kHz. "Hi-Res" when the file is at least 24-bit / 88.2 kHz. |
| Faux lossless | Lossless codec, but the spectrum carries a lossy encoder's wall, with a confidence of at least 75 %: probably a transcode. The likely source is named, e.g. "MP3 ≈192 kbps (V2)". |
| Suspect | The spectrum looks lossy, but the evidence is not strong enough to assert it: a wall between 19.1 and 21 kHz, where encoders and sample-rate converters cut alike, too little audio analysed, or a confidence under 75 %. |
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
  what each measurement means, the value found, the threshold it is held to, its
  weight in the probability, and whether it points to a lossy encoder (✘), reassures
  (✔), informs (ℹ) or limits the analysis (⚠).
- **Thème** switches between the classic and the dark theme. The change applies after
  a restart, which the app offers to do.

Run the tests (those that need ffmpeg are skipped when it cannot be found):

```shell
dotnet test
```

### Calibrating the weights

The weights of the probability can be fitted on lossless files you know to be genuine
(verified CD rips, purchases from Qobuz or Bandcamp…):

```shell
dotnet run -c Release --project LosslessOrNot.Calibration -- "D:\Verified lossless"
```

The tool draws 60 tracks at random (`--max-files`), analyses each one, transcodes the
analysed excerpt with ffmpeg (LAME, AAC, Vorbis and Opus at several bitrates; decoded
back into FLAC with and without dither, in 16 and 24 bits), and analyses every fake.
It writes `features.csv` and `report.md` into a `calibration-<date>` folder: detection
rate per encoder, compared with the rules this project started from; genuine files
that would be flagged; reliability of the probability; and the weights to copy into
`ConfidenceModel`. Nothing leaves your machine, and the transcodes are deleted as soon
as they are measured. Like the application, it analyses 45 s of each track in FFT
frames of 4096 points; `--window` and `--fft` change both.

Results of the calibration of 2026-10-05 — 173 genuine files, mostly loud electronic
music, and 3,460 transcodes of them:

| | Rules ported from Go | Current rules |
| --- | ---: | ---: |
| Genuine files called "Faux lossless" | 46 (27 %) | 3 (2 %) |
| Transcodes called "Faux lossless" | 39 % | 40 % (+ 20 % "Suspect") |
| MP3 128 / 192 kbps called "Faux lossless" | 45 % / 55 % | 96 % / 93 % |
| MP3 192 kbps decoded with noise-shaped dither | 5 % | 74 % |

The 39 other genuine files with a codec-like wall all have it between 19.1 and 21 kHz:
they are now "Suspect". The probability is reliable where it is high: with genuine
files and transcodes weighted equally, 96 % of the files given more than 90 % were
transcodes.

Analysing 90 s instead of 45 s moves these figures by less than a point. FFT frames of
8192 points do not help either: with levels kept on the 4096-point scale, walls and
floors measure the same, but over 186 ms frames the residue above an encoder's wall
seems to follow the music, and 17.7 % of the transcodes are "Suspect" instead of
20.2 %. The analysis therefore stays at 4096 points.

### Project structure

- `LosslessOrNot.Core` — the analysis engine, without any UI: FFT, verdict, calls to
  ffmpeg and ffprobe, final status.
- `LosslessOrNot` — the WinForms application.
- `LosslessOrNot.Tests` — xUnit tests, from pure FFT checks to end-to-end runs on files
  generated with ffmpeg.
- `LosslessOrNot.Calibration` — the console tool that fits the weights of the
  probability on genuine lossless files.

### Known limitations and next steps

- Encoders that keep the full band leave no spectral trace: at 44.1 kHz, AAC at
  256 kbps, LAME V0 or Vorbis q6 and above may pass everything up to Nyquist, and their
  transcodes are reported as lossless. Only an analysis of the encoder's MDCT frame
  grid could catch them — and tell a high-bitrate encoder's wall between 19.1 and
  21 kHz from a sample-rate converter's, which the spectrum cannot.
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
   Un encodeur avec perte coupe les aigus par un « mur » passe-bas et efface tout ce
   qui se trouve au-dessus ; le verdict « avec perte » n'est donc rendu que si trois
   signes concordent :
   - le niveau chute d'au moins 20 dB en 1 kHz — 15 dB s'il retombe sur un plancher
     plat ;
   - le mur se situe à 21 kHz ou en dessous ;
   - au-dessus, il ne reste rien de musical : le niveau retombe à −88 dB ou moins, ou au
     bruit d'arrondi du fichier (16 ou 24 bits), ou à un bruit constant qui ignore la
     musique (dither ajouté au décodage), ou à une image de la musique plus de 65 dB
     sous elle (ce que laisse filtrer le banc de filtres d'un décodeur), ou à une
     distorsion plate (le décodage d'un master très fort dépasse la pleine échelle et
     sature) — mais jamais à un résidu qui suit la musique de près tout en continuant
     de décroître, comme en laisse une atténuation naturelle.

   Trame par trame, l'analyse vérifie aussi que le mur tient jusque dans les passages
   les plus riches en aigus, et si le contenu au-delà de 21 kHz suit la musique : un tel
   contenu est hors de portée des encodeurs avec perte et désigne un vrai master
   lossless, alors qu'un dither ou un souffle ne prouvent rien. Dans les autres cas,
   l'analyse se déclare non concluante au lieu de deviner. Les seuils sont des
   fréquences absolues et non des fractions de la fréquence de Nyquist, pour ne pas
   condamner les vrais fichiers hi-res, qui n'ont rien au-delà de ~24 kHz.
5. Chaque indice ajoute son poids, en log-odds, à une probabilité que le fichier soit
   passé par un encodeur avec perte, comme le ferait un classifieur bayésien naïf. La
   confiance est cette probabilité pour un verdict « avec perte », et son complément
   pour un verdict « lossless » — plafonné à 90 %, car l'AAC à 256 kbps, LAME V0 ou
   Vorbis q6 et au-delà gardent souvent toute la bande et ne laissent aucune trace.
   Entre 19,1 et 21 kHz, le mur d'un encodeur à haut débit (MP3 320 kbps, V0, AAC,
   Opus, Vorbis) et celui d'un convertisseur de fréquence (un master produit en 48 kHz
   et livré en 44,1 kHz) sont indiscernables : la probabilité y est plafonnée à
   50–55 %, et le fichier est au mieux « Suspect ». Ces règles viennent de mesures
   (voir *Calibrer les poids*).
6. Le verdict spectral est croisé avec le codec annoncé pour donner le statut du
   fichier.

### Statuts

| Statut | Signification |
| --- | --- |
| Lossless authentique / Lossless Hi-Res | Codec lossless, avec du contenu au-delà de 20,5 kHz. « Hi-Res » si le fichier fait au moins 24 bits / 88,2 kHz. |
| Faux lossless | Codec lossless, mais le spectre porte le mur d'un encodeur avec perte, avec une confiance d'au moins 75 % : probablement un transcodage. La source probable est indiquée, par exemple « MP3 ≈192 kbps (V2) ». |
| Suspect | Le spectre ressemble à celui d'un encodeur avec perte, mais les indices ne suffisent pas pour l'affirmer : mur entre 19,1 et 21 kHz, où encodeurs et convertisseurs de fréquence coupent pareillement, trop peu d'audio analysé, ou confiance inférieure à 75 %. |
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
  critère : ce que mesure chaque point, la valeur trouvée, le seuil appliqué, son poids
  dans la probabilité, et s'il évoque un encodeur avec perte (✘), s'il est rassurant
  (✔), informatif (ℹ) ou s'il limite l'analyse (⚠).
- **Thème** permet de passer du thème classique au thème sombre. Le changement
  s'applique après un redémarrage, que l'application propose de faire.

Lancer les tests (ceux qui ont besoin de ffmpeg sont ignorés s'il est introuvable) :

```shell
dotnet test
```

### Calibrer les poids

Les poids de la probabilité peuvent être ajustés sur des fichiers lossless dont vous
êtes sûr (rips CD vérifiés, achats Qobuz ou Bandcamp…) :

```shell
dotnet run -c Release --project LosslessOrNot.Calibration -- "D:\Vrais lossless"
```

L'outil tire 60 morceaux au hasard (`--max-files`), analyse chacun, transcode l'extrait
analysé avec ffmpeg (LAME, AAC, Vorbis et Opus à plusieurs débits ; décodés en FLAC avec
et sans dither, en 16 et 24 bits) et analyse chaque transcodage. Il écrit
`features.csv` et `report.md` dans un dossier `calibration-<date>` : taux de détection
par encodeur, comparé aux règles d'origine du projet ; vrais lossless qui seraient
signalés ; fiabilité de la probabilité ; et les poids à reporter dans
`ConfidenceModel`. Rien ne quitte votre machine, et les transcodages sont supprimés dès
qu'ils sont mesurés. Comme l'application, il analyse 45 s de chaque morceau en trames FFT
de 4096 points ; `--window` et `--fft` changent l'un et l'autre.

Résultats de la calibration du 5 octobre 2026 — 173 vrais lossless, surtout de la
musique électronique très forte, et 3 460 transcodages de ces fichiers :

| | Règles portées de Go | Règles actuelles |
| --- | ---: | ---: |
| Vrais lossless jugés « Faux lossless » | 46 (27 %) | 3 (2 %) |
| Transcodages jugés « Faux lossless » | 39 % | 40 % (+ 20 % « Suspect ») |
| MP3 128 / 192 kbps jugés « Faux lossless » | 45 % / 55 % | 96 % / 93 % |
| MP3 192 kbps décodé avec dither à mise en forme de bruit | 5 % | 74 % |

Les 39 autres vrais lossless qui portent un mur d'encodeur l'ont tous entre 19,1 et
21 kHz : ils sont désormais « Suspect ». La probabilité est fiable là où elle est
haute : vrais lossless et transcodages pesant autant, 96 % des fichiers jugés à plus
de 90 % étaient des transcodages.

Analyser 90 s au lieu de 45 s déplace ces chiffres de moins d'un point. Des trames FFT
de 8192 points n'aident pas davantage : avec des niveaux gardés sur l'échelle de
4096 points, murs et planchers se mesurent de même, mais sur des trames de 186 ms le
résidu au-dessus du mur d'un encodeur semble suivre la musique, et 17,7 % des
transcodages sont « Suspect » au lieu de 20,2 %. L'analyse reste donc à 4096 points.

### Structure du projet

- `LosslessOrNot.Core` — le moteur d'analyse, sans interface : FFT, verdict, appels à
  ffmpeg et ffprobe, statut final.
- `LosslessOrNot` — l'application WinForms.
- `LosslessOrNot.Tests` — les tests xUnit, des vérifications de la FFT jusqu'aux tests
  de bout en bout sur des fichiers générés avec ffmpeg.
- `LosslessOrNot.Calibration` — l'outil en ligne de commande qui ajuste les poids de la
  probabilité sur de vrais lossless.

### Limites connues et prochaines étapes

- Les encodeurs qui gardent toute la bande ne laissent aucune trace spectrale : à
  44,1 kHz, l'AAC à 256 kbps, LAME V0 ou Vorbis q6 et au-delà peuvent tout laisser
  passer jusqu'à Nyquist, et leurs transcodages sont jugés lossless. Seule une analyse
  de la grille MDCT de l'encodeur pourrait les repérer — et distinguer le mur d'un
  encodeur à haut débit, entre 19,1 et 21 kHz, de celui d'un convertisseur de
  fréquence, ce que le spectre ne permet pas.
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
