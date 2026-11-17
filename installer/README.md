# Пакет установщика

Здесь лежит исходник установщика (`setup.iss`) для Inno Setup 6.

Чтобы собрать установщик, нужна такая раскладка файлов относительно `setup.iss`:

```
installer/
  setup.iss
  app/        <- WoWStickBridge.exe, ViGEmClient.dll, run*.cmd, run-hidden.vbs,
                 stop.cmd, README.md, LICENSES.txt
  licenses/   <- ViGEmBus-LICENSE.txt, ViGEmClient-LICENSE.txt
  vendor/     <- ViGEmBus_1.22.0_x64_x86_arm64.exe
```

Готовые бинарники и драйвер не хранятся в репозитории — они лежат в
[Releases](https://github.com/Follen22/wow-stick-bridge/releases) (Setup и Portable zip).
Скопируй содержимое Portable-сборки в `app/` (без `Install-Driver.cmd`,
`PORTABLE-README.txt` и `vendor/`), а `vendor\ViGEmBus_1.22.0_x64_x86_arm64.exe`
возьми из официального релиза ViGEmBus.

Сборка:

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
```

Результат: `output\WoWStickBridge-Setup-<версия>.exe`.
