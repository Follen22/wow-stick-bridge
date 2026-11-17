# Исходники

`WoWStickBridge.cs` — вся утилита одним файлом (C#, .NET Framework 4.x, без SDK).

Сборка:

```
build.cmd
```

или вручную:

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /platform:x64 /optimize+ /out:WoWStickBridge.exe WoWStickBridge.cs
```

Рядом с exe должен лежать `ViGEmClient.dll` (x64, MIT, © Nefarius Software Solutions e.U.;
можно взять из portable-сборки в Releases). Для работы нужен установленный драйвер ViGEmBus.
