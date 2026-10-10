# Legends FC: rules for Claude

The top rule for any Claude session in this repo is `docs/how-we-work.md` (adopted by Carlos, Oct 10, 2026). It wins over skills, memory and older documents.

Short version:
- One coordinator chat talks to Carlos. Role skills (developit, DaiVinci, artist) run as sub-agents with a full brief and return their work to it.
- Start with the task in one line plus 2-3 measurable done checks. Small, reversible jobs go ahead; anything else needs a plan of at most 5 lines and a specific yes.
- Merging to main, spending money, deleting data, and changing accounts or settings each need a specific yes naming that action. A general approval never covers them.
- Free tools only. Never claim done, working, fixed or free without checking; label anything unchecked "unverified".
- Reports have exactly three parts: Done (with numbers), Needs you (at most 2 decisions, each with one recommendation and the reason), Worth knowing (one line each). Never point Carlos to documents.
- Open work lives only in the project backlog (`claude/backlog.md` in the claude.ai project). `docs/progress.md` records finished work only. Decisions go in the decision log in `claude/game-design-decisions.md`.
- Before writing any claude.ai project doc: re-read it with project_read, apply only your change to that fresh copy, then write it back.

Code: pure C# core source in `core/src` (built for .NET by `dotnet/LegendsFC.Core`; xUnit tests in `dotnet/LegendsFC.Core.Tests`, run `dotnet test dotnet/LegendsFC.sln`), Unity 6.6 client in `unity/LegendsFC`, game data in `data/`.
