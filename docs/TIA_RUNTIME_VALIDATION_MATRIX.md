# TIA Runtime Validation Matrix

Ten dokument jest przygotowaną macierzą walidacji do wykonania na realnych stacjach z TIA Portal. Na tej stacji potwierdzono lokalnie instalację i discovery dla V19. Pozostałe pola dla V15, V16, V17, V18 i Latest nadal wymagają uzupełnienia po uruchomieniu na rzeczywistych instalacjach.

## Scope

- Application runtime: .NET Framework 4.8
- Discovery mode: Auto and Manual
- Providers in code: `V15OpennessVersionProvider`, `V16OpennessVersionProvider`, `V17OpennessVersionProvider`, `V18OpennessVersionProvider`, `V19OpennessVersionProvider`, `LatestOpennessVersionProvider`
- Key contracts: project attach, `Modified`/`IsModified`, path read, `Save()`, `Archive(...)`, runtime selection fallback

## Station Matrix

| TIA Version | Station Name | PublicAPI Path | Expected Provider | Auto Selection | Manual Selection | Verified |
| --- | --- | --- | --- | --- | --- | --- |
| V15 | pending | pending | `V15OpennessVersionProvider` | pending | pending | pending |
| V16 | pending | pending | `V16OpennessVersionProvider` | pending | pending | pending |
| V17 | pending | pending | `V17OpennessVersionProvider` | pending | pending | pending |
| V18 | pending | pending | `V18OpennessVersionProvider` | pending | pending | pending |
| V19 | DESKTOP-S5T00FK | `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll` | `V19OpennessVersionProvider` | pending, process not running | runtime `V19` persisted and discoverable | partial, installation and discovery verified |
| Latest | pending | pending | `LatestOpennessVersionProvider` | pending | pending | pending |

## Validation Checklist Per Station

1. Confirm TIA Portal starts and exposes Openness attach dialog.
2. Confirm `Check TIA Connection` selects the expected runtime and provider.
3. Confirm the UI diagnostic line shows provider name and runtime-selection reason.
4. Confirm `Sync Project From TIA` reads the correct project path.
5. Confirm unsaved-state detection matches TIA UI state.
6. Confirm `Archive Now` completes with a valid archive artifact.
7. Confirm logs include selected runtime version, assembly path and provider name.

## Detailed Results

| TIA Version | Scenario | Expected Result | Observed Result | Verified By | Date |
| --- | --- | --- | --- | --- | --- |
| V15 | Auto mode with running V15 process | Runtime `V15`, provider `V15OpennessVersionProvider` | pending | pending | pending |
| V15 | Manual mode forced to `V15` | Runtime `V15`, provider `V15OpennessVersionProvider` | pending | pending | pending |
| V15 | Unsaved state | `Modified` reflected correctly | pending | pending | pending |
| V15 | Archive | `ProjectArchiveMode` or `ProjectArchivationMode` path succeeds | pending | pending | pending |
| V16 | Auto mode with running V16 process | Runtime `V16`, provider `V16OpennessVersionProvider` | pending | pending | pending |
| V16 | Manual mode forced to `V16` | Runtime `V16`, provider `V16OpennessVersionProvider` | pending | pending | pending |
| V16 | Unsaved state | `Modified` reflected correctly | pending | pending | pending |
| V16 | Archive | archive contract succeeds | pending | pending | pending |
| V17 | Auto mode with running V17 process | Runtime `V17`, provider `V17OpennessVersionProvider` | pending | pending | pending |
| V17 | Path read | `Path` or equivalent returns project path | pending | pending | pending |
| V18 | Auto mode with running V18 process | Runtime `V18`, provider `V18OpennessVersionProvider` | pending | pending | pending |
| V18 | Save and archive | standard archivation mode succeeds | pending | pending | pending |
| V19 | Local installation and discovery on DESKTOP-S5T00FK | PublicAPI present at `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll`, assembly version `19.0.0.0`, runtime catalog returns `V19` from configured path | verified locally; TIA process was not running during inspection | GitHub Copilot | 2026-03-30 |
| V19 | Auto mode with running V19 process | Runtime `V19`, provider `V19OpennessVersionProvider` | pending, TIA process not running during current validation | pending | pending |
| V19 | Runtime compatibility | launcher remains on .NET Framework 4.8 | verified locally by successful solution build and runtime discovery against V19 assembly | GitHub Copilot | 2026-03-30 |
| Latest | Auto mode with running latest process | Runtime latest, provider `LatestOpennessVersionProvider` | pending | pending | pending |
| Latest | Path read fallback | `Location` or standard path property succeeds | pending | pending | pending |

## Provider Notes To Update After Real Validation

- `V15OpennessVersionProvider`: confirm final archive enum/type combination used in production.
- `V16OpennessVersionProvider`: confirm whether `Modified` remains primary over `IsModified`.
- `V17OpennessVersionProvider`: confirm no additional path aliases are needed.
- `V18OpennessVersionProvider`: confirm archive behavior is identical to V17.
- `V19OpennessVersionProvider`: confirm no extra runtime incompatibility cases beyond known .NET mismatch.
- `LatestOpennessVersionProvider`: confirm whether `Location` is required or only fallback.