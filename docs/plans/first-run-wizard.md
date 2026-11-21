# Plan: first-run setup guide ("wizard")

Status: **built in 0.4.0.** Decisions: keep screen 6 (some people won't be confident going into Settings);
first install only (existing users run it from Settings); keep all 8 screens.

## Goal
When Craft Library is opened for the first time, a short guide walks the person through the main choices,
one screen at a time, in plain words. It ends with the app ready to use and looking the way they like.
Everything it sets can be changed later in Settings, and the guide says so.

## Principles (from the app's rules)
- Short: 7 screens, each with one idea, each readable in a few seconds.
- Plain words: say what it does for *them*, never how it works. No terms like "thumbnail cache" or "file association".
- Nothing is decided for them: every choice starts on the calm default, and they can skip any screen or the whole guide.
- Changes show straight away in the app behind the guide, so they can see what they're choosing.
- Stitched buttons, same colours and fonts as the rest of the app; it follows the text size they pick.
- Big, obvious **Back** and **Next** buttons, "Step 3 of 7" at the top, and **Skip** on every screen.

## When it appears
- On the very first start (no craft folder chosen yet). This replaces today's "Choose craft folder" card.
- Not for people who already use the app (they already have a craft folder): for them the guide is offered
  from the Menu and from Settings as **"Run the setup guide again"**.
- If they close the app halfway, it reopens at the same screen next time.

## The screens (draft wording)

**1. Welcome**
> Welcome to Craft Library
> This app helps you find, sort and tidy your craft files: papers, cut files, fonts and more.
> Let's set it up the way you like. It takes about two minutes, and you can change anything later in Settings.
> [Let's start] [Skip for now]

**2. Your craft folder** *(the only screen that can't be skipped, because the app needs a folder to work)*
> Where are your craft files?
> Choose the folder that holds your craft files. The app only ever looks inside the folders you choose, so
> nothing else on the computer is touched.
> [Choose craft folder]
>
> Where do new files arrive?
> Files you download usually land in your Downloads folder. The app shows them in "To sort", ready to put away.
> [Downloads ✓] [Choose a different folder]
>
> You can add more places later, like a USB stick or portable drive.

**3. Easy to read**
> Text size: how big the words are. [Small] [Medium] [Large]
> Font: the style of the letters.
> - Figtree: clean and simple (standard)
> - Atkinson Hyperlegible: designed so every letter is easy to tell apart
> - Lexend: wider letters with more space, easy on tired eyes
> - OpenDyslexic: shaped to help some people with dyslexia
> *(Each name is written in its own font, so they can see it.)*

**4. Colours**
> Light or dark: light is bright like paper; dark is easier on the eyes in the evening. [Light] [Dark]
> Colour strength: how strong the colours are. [Soft (pastel)] [Muted] [Bold]
> Main colour: used for buttons and highlights. *(the 13 colour circles)*
> Background: a gentle tint behind everything. *(the 6 tint circles)*

**5. How your files look**
> Show files as: [Pictures in a grid] [A list with details]
> Picture size: how big each file's picture is. [Small] [Medium] [Large] [Extra large]
> Show under each file: [✓ Name] [✓ Size] [ Date added] [ File type]
> Put files in order by: [Name] [Newest first] [File type] [Largest first]

**6. Opening your files**
> When you double-click a file, which program should open it?
> "Windows' choice" means the program your computer already uses for that kind of file.
> Only the craft file types found in their folder are listed (for example: pictures, PDFs, Word documents,
> SVG cut files, Silhouette files), each with: [Windows' choice (Photos)] [Choose a program…]

**7. Comfort and safety**
> These are all optional. Turn on any that sound helpful.
> - Ask before removing files *(on)*: asks "Are you sure?" before anything goes to the Recycle Bin.
> - Messages disappear by themselves: messages at the bottom fade away after a few seconds.
> - Explain buttons when I point at them: a short note appears when you rest the mouse on a button.
> - Open where I left off: the app reopens in the same folder, with the same file picked.
> - Stronger contrast: darker text and lines, easier to see.

**8. All done**
> You're ready to go!
> A short summary of their choices, each with a [Change] link back to its screen.
> Tip: everything here is also in Menu › Settings.
> [Start using Craft Library]

## How it would be built
- `Views/SetupGuideView.xaml`: a full-window overlay (like the other pop-ups), one panel per screen.
  Clicking outside does nothing, so nothing is lost by accident; Esc acts as Back.
- `ViewModels/SetupGuideViewModel.cs`: the current step, Back/Next/Skip, and the choices. It reuses the
  option lists already in `SettingsViewModel` (same colours, fonts and sizes), so the two can never disagree.
- Choices are saved as they're made (the same `SettingsChanged()` path Settings uses), so the app behind
  updates live and nothing is lost if the app closes.
- `AppData` gets `SetupGuideDone` (true for anyone who already has a craft folder) and `SetupGuideStep`
  (to reopen at the same screen).
- "Run the setup guide again" in the Menu and at the top of Settings.
- Tests: the new saved fields load and save, and existing users skip the guide.
- About one working session to build and test.

## Questions to decide
1. Should screen 6 (opening files) be in the guide? It's useful but the most technical screen; it could stay in
   Settings only.
2. Should the guide be offered to existing users once after updating (for example a gentle "Try the new setup
   guide?" message), or only from the Menu?
3. Are any of the screens too much? Easy cuts if it needs to be shorter: screen 5's "Show under each file", or
   screen 7.
