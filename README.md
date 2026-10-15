# USB Copy Station

Windows aplikacija koja isti sadržaj upisuje na više USB stikova istovremeno. Radi kao hardverski duplikator: pripremi se jedan image, upiše se na sve utaknute stikove, a svaki stik se zatim proveri čitanjem direktno sa uređaja.

Operater samo menja stikove. Svaki fizički USB port je slot sa stalnim brojem, a kartica slota pokazuje napredak i rezultat (zeleno OK, crveno greška).

Detaljno uputstvo za upotrebu je u [docs/UPUTSTVO.md](docs/UPUTSTVO.md).

## Mogućnosti

- **Image od foldera.** Iz izabranog foldera pravi se fixed VHD (MBR, jedna particija) u NTFS, exFAT ili FAT32 formatu. Podrazumevani format je NTFS. Može se koristiti i gotov `.img` ili fixed `.vhd`.
- **Raw upis.** Image se upisuje direktno na `\\.\PhysicalDriveN` u blokovima od 4 MB, bez keša (`FILE_FLAG_NO_BUFFERING | WRITE_THROUGH`). Pre upisa se volumeni stika zaključavaju i demontiraju.
- **Prava provera.** Upisani deo se čita sa flash-a, ne iz OS keša, i poredi bajt po bajt sa image-om. Tako se hvataju neispravni stikovi i stikovi lažnog kapaciteta.
- **Automatska detekcija.** Prate se samo USB diskovi, uz hotplug obaveštenja. Sistemski disk, disk izvora i disk sa radnim folderom su uvek izuzeti.
- **Slotovi po fizičkom portu.** Slot se vezuje za port (`DEVPKEY_Device_LocationPaths`), ne za slovo diska, i broj slota se pamti.
- **Dva režima rada.** U ručnom režimu *Start* upisuje sve utaknute stikove. U automatskom režimu svaki stik se upisuje čim se utakne.
- **Jedinstven serijski broj po stiku.** Svaki stik dobija sopstveni MBR disk signature i serijski broj volumena. Pokriveni su i rezervni boot sektori (NTFS, FAT32) i exFAT checksum boot regiona. Bez ovoga Windows pri istovremenom priključenju dva stika iz iste serije jedan stavlja offline.
- **Zaštite.** Postoji gornja granica veličine diska (da se ne pregazi eksterni HDD). Stikovi koji već imaju podatke se preskaču ili traže potvrdu.
- **Evidencija.** Postoji CSV log po danu: slot, port, model i serijski broj stika, SHA-256 image-a, rezultat, trajanje i brzina.
- **Paralelizam.** Do 64 stika istovremeno; podrazumevano je 16.

## Zahtevi

- Windows 10 ili 11, x64
- Administratorska prava (traže se preko UAC-a pri pokretanju)
- Za build: .NET 8 SDK. Gotov exe je self-contained i ne traži instaliran .NET.

## Zašto administratorska prava

Raw upis na fizički disk, zaključavanje i demontiranje volumena i diskpart dozvoljeni su samo administratoru. Windows tu nema izuzetak; ni grupa Power Users ni polise za removable media ne daju takav pristup. Aplikacija zato u manifestu traži `requireAdministrator`.

## Build

Gotov exe se ne objavljuje. Svako pravi svoj iz izvornog koda, pa tačno zna šta pokreće.

1. Instaliraj **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0 (Windows, x64).
2. Preuzmi kod:
   ```
   git clone https://github.com/sreckojovancevic/UsbCopyStation.git
   cd UsbCopyStation
   ```
   Kod možeš preuzeti i bez git-a: na GitHub-u *Code → Download ZIP*, pa raspakuj.
3. Pokreni **`publish.cmd`**, dvoklikom ili iz komandne linije.

Rezultat je **`dist\UsbCopyStation.exe`**: jedan fajl koji radi i na računaru bez instaliranog .NET-a. Taj exe možeš da kopiraš gde hoćeš.

Prvi build traje malo duže jer `dotnet` skida potrebne pakete; za to treba internet.

## Struktura projekta

```
Native/Win32.cs          P/Invoke: CreateFile, DeviceIoControl, SetupDi, CfgMgr, volumeni
Core/DiskEnumerator.cs   detekcija diskova, bus type, fizički port, izbacivanje
Core/VolumeUtil.cs       volumeni po disku, lock/dismount, izuzeti diskovi
Core/ImageBuilder.cs     folder → fixed VHD preko diskpart-a
Core/ImageInfo.cs        prepoznavanje image-a (raw / fixed VHD), SHA-256
Core/DiskLayout.cs       MBR i fajl sistem image-a, jedinstveni serijski brojevi
Core/WriteJob.cs         raw upis i provera jednog stika
Core/SlotState.cs        stanje slota (napredak, faza)
Core/StationLog.cs       CSV evidencija
Core/Settings.cs         podešavanja i mapa port → slot
UI/MainForm.cs           glavni prozor, režimi, hotplug
UI/SlotControl.cs        kartica slota
```

## Ograničenja

- Image se upisuje u svojoj veličini, a ostatak stika ostaje nealociran. Image se pravi taman koliko treba sadržaju, pa staje i na najmanji stik u seriji.
- FAT32: fajl najviše 4 GB, image najviše 32 GB (ograničenje diskpart-a).
- VHDX i dynamic VHD nisu podržani.
- Provera hvata lažni kapacitet samo u opsegu koji image zauzima.
- Brzinu najčešće ograničava upis jeftinog flash-a i deljeni propusni opseg hub-a. USB 2.0 hub deli 480 Mbps na sve portove.

## Status

Verzija 0.1.1, rana verzija. Build je proveren. Promena serijskog broja testirana je na FAT32, exFAT i NTFS image-ima i fsck je čist posle izmene. Upis na veći broj stvarnih stikova tek treba testirati u radu.

## Autor

**Srećko Jovančević** — [github.com/sreckojovancevic](https://github.com/sreckojovancevic)

## Acknowledgments

- **Claude (Anthropic)** — elaboracija koda i proširenje kodne baze: Win32 sloj, raw upis i provera, detekcija uređaja i slotova, rad sa image-ima i fajl sistemima, UI.
