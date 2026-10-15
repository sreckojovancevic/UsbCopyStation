# Uputstvo za upotrebu

## 1. Pokretanje

Pokreni `UsbCopyStation.exe`. Windows traži administratorsku dozvolu (UAC); bez nje aplikacija se ne pokreće.

Podešavanja, radni folder i logovi su u `%LOCALAPPDATA%\UsbCopyStation`.

## 2. Priprema image-a

Svaka serija počinje pripremom image-a. To je sadržaj koji će biti upisan na sve stikove.

### Varijanta A: od foldera

1. Izaberi **Folder → napravi image** i folder sa sadržajem.
2. Izaberi fajl sistem:
   - **NTFS** (podrazumevano): za Windows računare, bez ograničenja veličine fajla.
   - **exFAT**: kad stik ide na razne uređaje (Mac, TV, auto radio), uz fajlove veće od 4 GB.
   - **FAT32**: za stare uređaje i bootabilne medije. Fajl može imati najviše 4 GB, a image najviše 32 GB.
3. Upiši **Labelu**, odnosno ime stika koje se vidi u Exploreru.
4. Proveri **Radni folder**, gde nastaje privremeni image. Mora biti na lokalnom disku, sa dovoljno mesta, i ne sme biti unutar izvornog foldera.
5. Klikni **Pripremi image**.

Aplikacija napravi virtuelni disk, formatira ga, iskopira fajlove i izračuna SHA-256. Kad završi, ispod dugmeta piše zelenim: ime, format, fajl sistem, veličina i hash.

### Varijanta B: gotov image

1. Izaberi **Gotov image** i fajl (`.img`, `.bin` ili fixed `.vhd`).
2. Klikni **Pripremi image**. Aplikacija proveri format i izračuna SHA-256.

## 3. Opcije

| Opcija | Šta radi |
|---|---|
| **Režim** | *Ručno*: upis kreće na dugme Start. *Automatski*: svaki utaknut stik se odmah upisuje. |
| **Maks. stik GB** | Disk veći od ovoga se nikad ne upisuje. Štiti eksterne hard diskove od slučajnog brisanja. |
| **Paralelno** | Najveći broj stikova koji se upisuju istovremeno. |
| **Provera posle upisa** | Čita stik direktno sa flash-a i poredi sa image-om. Preporučeno je da ostane uključeno. |
| **Jedinstven serijski broj** | Svaki stik dobija svoj potpis. Preporučeno je da ostane uključeno. |
| **Dozvoli stikove sa podacima** | Kad je isključeno, stikovi koji već imaju particije se ne diraju automatski. |
| **Izbaci stik kad završi** | Posle uspeha stik se bezbedno izbacuje i odmah se može izvaditi. |

## 4. Slotovi

Svaki USB port na kom se pojavi stik dobija **broj slota**. Broj ostaje isti i kad se stik zameni, i posle ponovnog pokretanja aplikacije. Najbolje je da portove na hubu jednom obeležiš brojevima slotova.

Ako se hub prespoji ili promeni raspored, klikni **Resetuj slotove**. Numeracija tada kreće ispočetka, redom kojim se stikovi utaknu.

Boje kartice:

| Boja | Značenje |
|---|---|
| svetlosiva | prazno, ili stik ne može da se koristi (razlog piše na kartici) |
| bela | spreman |
| žuta | stik ima podatke |
| plava | upis u toku |
| ljubičasta | provera u toku |
| zelena | OK, može da se izvadi |
| crvena | greška (detalj je u tooltip-u i logu) |

Desni klik na karticu nudi **Upiši ovaj stik**, **Otkaži** i **Izbaci stik**.

## 5. Rad

### Ručni režim

1. Utakni stikove.
2. Sačekaj da kartice budu bele ("Spreman").
3. Klikni **Start**. Ako neki stikovi imaju podatke, aplikacija pita da li i njih da pregazi.
4. Kad su sve kartice zelene ili crvene, izvadi stikove.

### Automatski režim

1. Izaberi automatski režim i klikni **Uključi automatski**.
2. Utakni stik. Upis kreće sam.
3. Kad kartica pozeleni, izvadi stik i utakni sledeći.

Stik koji je upisan neće biti upisan ponovo dok je u portu. Kad se izvadi i utakne novi, ciklus kreće ispočetka.

### Prekid

**Stop sve** otkazuje sve upise u toku i isključuje automatski režim. Stik kom je upis prekinut ostaje nepotpun i treba ga upisati ponovo.

## 6. Log

**Log folder** otvara folder sa CSV fajlom za svaki dan. Svaki stik je jedan red: vreme, slot, port, model, serijski broj stika, image i njegov SHA-256, rezultat, trajanje upisa i provere, brzina i poruka. Fajl se otvara u Excel-u (separator je `;`).

## 7. Česti problemi

| Poruka | Uzrok i rešenje |
|---|---|
| *Premali* | Stik je manji od image-a. Koristi veći stik ili manji sadržaj. |
| *Prevelik* | Disk je veći od podešene granice. Ako je to zaista stik, povećaj **Maks. stik GB**. |
| *Izuzet (sistem/izvor)* | Na tom disku je sistem, izvorni folder ili radni folder. |
| *Volumen je u upotrebi* | Neki program drži otvoren fajl sa stika (Explorer, antivirus). Zatvori ga i pokušaj ponovo. |
| *Provera nije prošla* | Stik je neispravan ili ima lažan kapacitet. Odvoji ga. |
| *Stik je izvađen tokom rada* | Stik je izvađen pre kraja. Upiši ga ponovo. |
| Upis je spor | Usko grlo je obično flash ili hub. Smanji **Paralelno** ili rasporedi stikove na više hub-ova i USB kontrolera. |
