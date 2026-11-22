# Arknights Recruitment Helper

An  Windows tool that captures the Arknights PC recruitment window, runs Windows OCR, and shows guaranteed 4-star and 5-star recruitment results. Top Operator combinations include matching 6-star operators.

## Requirements

- Windows 10 version 19041 or newer, or Windows 11
- x64 Windows
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- The Windows English OCR language pack
- Internet access to load recruitment data from Krooster

## Use

1. Install the English OCR language pack in Windows language settings.
2. Start Arknights and open the recruitment screen.
3. Start `ArknightsRecruitTool.exe`.
4. Press **F8** to capture and scan.
  **The Robot tag is ignored and will not show up on the app window**

The app finds the process named `Arknights`, reads its window bounds, captures that rectangle, and runs Windows OCR locally. It does not inject code into or modify the game process. 

## Build

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## Data source

Recruitment operator and tag data is fetched from Krooster at application startup:

https://www.krooster.com/tools/recruit

The app also saves the latest diagnostic capture at:

`%LOCALAPPDATA%\ArknightsRecruitTool\ArknightsRecruitTool\1.0.0\ArknightsRecruitTool\last-capture.png`

## Disclaimer

This project is not affiliated with or endorsed by Hypergryph, Yostar, Krooster, or Aceship.
