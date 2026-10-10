# Legends FC

Offline mobile football game for phones and tablets: club management (career, transfers, training, facilities) plus a playable 3D match.

## Layout
| Folder | What it is |
|---|---|
| `core/` | Pure C# game logic (no UnityEngine). It's a local Unity package and is also compiled by `dotnet/`. |
| `dotnet/` | `LegendsFC.Core` (netstandard2.1 build of `core/src`) and `LegendsFC.Core.Tests` (xUnit). |
| `data/` | Game data: `rules/` (JSON rule sets), `config/` (tuning constants, each with its source). |
| `unity/` | The Unity 6 project (match, screens). Created on the Windows PC. |
| `tools/balancing/` | Balancing simulations (Python). |
| `docs/progress.md` | Finished work only (open work lives in the project backlog). |
| `docs/how-we-work.md` | How every chat and sub-agent works on this project (top rule). |

## Run the tests
```
dotnet test dotnet/LegendsFC.sln
```
CI runs the same command on every push and pull request.
