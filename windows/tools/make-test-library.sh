#!/bin/bash
# Builds a pretend craft library (papers, cut files, Word, zip, PDF, fonts, duplicates) inside a Wine
# "computer", and points the app at it. Needs ImageMagick and zip.
set -e
W="${1:?usage: make-test-library.sh <wine-prefix>}"; U="$W/drive_c/users/root"
C="$U/Documents/Craft Files"; D="$U/Downloads"
rm -rf "$C" "$D"; mkdir -p "$C/Patterned Papers/Christmas" "$C/Patterned Papers/Florals" "$C/Patterned Papers/Kraft & Basics" "$C/Card Patterns" "$C/Cut Files (SVG)/Valentines" "$C/Cut Files (SVG)/Boxes & 3D" "$C/Project Kits" "$C/Fonts" "$C/Old laptop backup" "$D"
T=$(mktemp -d)
DOTS=$(for x in $(seq 45 90 900); do for y in $(seq 45 90 900); do printf "circle %d,%d %d,%d " $x $y $((x+14)) $y; done; done)
convert -size 900x900 xc:'#9E1B2A' -fill '#F3DE8A' -draw "$DOTS" "$C/Patterned Papers/Christmas/Christmas Poinsettia Paper - Red.jpg"
STARS=$(for i in $(seq 1 140); do x=$((RANDOM%900)); y=$((RANDOM%900)); printf "circle %d,%d %d,%d " $x $y $((x+3)) $y; done)
convert -size 900x900 xc:'#1E2E52' -fill '#F6F1D5' -draw "$STARS" "$C/Patterned Papers/Christmas/Christmas Starry Night Paper.jpg"
convert -size 1200x900 plasma:pink-white -blur 0x6 "$C/Patterned Papers/Florals/Pink Blossom Paper.png"
convert -size 900x900 pattern:checkerboard -scale 400% -fill '#A9C6EE' -opaque black "$C/Patterned Papers/Florals/Blue Gingham.jpg"
convert -size 900x900 plasma:'#C9A57A'-'#B88A5A' -blur 0x1 "$C/Patterned Papers/Kraft & Basics/Kraft Speckle Paper.jpg"
for i in 2 10 1; do convert -size 600x800 gradient:'#F2B5C4'-'#C4B3EE' "$C/Card Patterns/Card $i.jpg"; done
heart='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 21s-7-4.5-9.5-9A5 5 0 0 1 12 6a5 5 0 0 1 9.5 6c-2.5 4.5-9.5 9-9.5 9z" fill="#F2A9A0" stroke="#9A3D58" stroke-width="0.6"/></svg>'
echo "$heart" > "$C/Cut Files (SVG)/Valentines/Heart Shaker Card - Cut File.svg"
echo '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect x="10" y="20" width="80" height="60" fill="none" stroke="#0E6B64" stroke-width="2"/><path d="M10 20 L50 5 L90 20" fill="none" stroke="#0E6B64" stroke-width="2" stroke-dasharray="4 2"/></svg>' > "$C/Cut Files (SVG)/Boxes & 3D/Pillow Box Template.svg"
echo '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 2l3 7 7 .5-5.5 4.5 2 7-6.5-4-6.5 4 2-7L2 9.5 9 9z" fill="#F3DE8A" stroke="#7A5B12"/></svg>' > "$D/download (3).svg"
printf 'DXF placeholder' > "$C/Cut Files (SVG)/Boxes & 3D/Gift Box.dxf"
# Word document holding a paper image
mkdir -p $T/docx/word/media $T/docx/_rels; cp "$C/Patterned Papers/Christmas/Christmas Poinsettia Paper - Red.jpg" $T/docx/word/media/image1.jpeg
echo '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>' > "$T/docx/[Content_Types].xml"
echo '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>' > $T/docx/word/document.xml
(cd $T/docx && zip -qr "$C/Patterned Papers/Christmas/Christmas Poinsettia - Print Sheet.docx" .)
(cd $T/docx && zip -qr "$D/Document12.docx" .)
# Zip bundle with 6 files
mkdir -p $T/zip; for i in 1 2 3 4 5; do convert -size 300x300 plasma: $T/zip/paper$i.jpg; done; echo hi > $T/zip/readme.txt
(cd $T/zip && zip -qr "$D/bundle_8812.zip" .)
convert -size 595x842 xc:white -fill '#0E6B64' -draw "rectangle 60,60 535,300" -fill black -pointsize 40 -draw "text 80,380 'Winter Village Kit'" "$C/Project Kits/Winter Village 3D Kit.pdf" 2>/dev/null || true
convert -size 900x700 plasma:orange-yellow "$D/paper pack.psd" 2>/dev/null || true
cp $(dirname "$0")/../CraftLibrary.App/Fonts/lexend-400.ttf "$D/BeautifulScript.ttf"
cp $(dirname "$0")/../CraftLibrary.App/Fonts/opendyslexic-400.ttf "$C/Fonts/Typewriter Notes.ttf"
cp $(dirname "$0")/../CraftLibrary.App/Fonts/atkinson-hyperlegible-700.ttf "$C/Fonts/Chunky Marker.ttf"
# Things to sort, with copies of files already in the library
cp "$C/Patterned Papers/Christmas/Christmas Poinsettia Paper - Red.jpg" "$D/IMG_4432.jpg"
cp "$C/Patterned Papers/Christmas/Christmas Poinsettia Paper - Red.jpg" "$C/Old laptop backup/IMG_4432 (1).jpg"
cp "$C/Cut Files (SVG)/Valentines/Heart Shaker Card - Cut File.svg" "$C/Old laptop backup/Heart Shaker Card - Cut File.svg"
convert -size 900x900 xc:white -fill '#A8D5B0' -draw "polygon 0,0 120,0 900,780 900,900 780,900 0,120" "$D/freebie.png"
printf 'Silhouette' > "$D/7f3a9c.studio3"; printf 'mystery' > "$D/mystery.cut"; printf 'MZ' > "$D/setup.exe"
for i in $(seq 1 60); do convert -size 400x400 plasma: "$C/Patterned Papers/Florals/Floral $i.jpg"; done
rm -rf $T
# The app's saved data: craft folder and "To sort" already chosen
A="$U/AppData/Local/Craft Library"; mkdir -p "$A"
cat > "$A/data.json" <<JSON
{ "locations": [ { "key": "C:\\\\users\\\\root\\\\Documents\\\\Craft Files", "label": "Craft Files", "path": "C:\\\\users\\\\root\\\\Documents\\\\Craft Files" } ],
  "inboxPath": "C:\\\\users\\\\root\\\\Downloads" }
JSON
echo "library ready: $(find "$C" "$D" -type f | wc -l) files"
