# LPR381 Project

Group project for LPR381 (Linear Programming). A menu-driven `solve.exe` that reads an LP/IP from a
text file, solves it with a chosen algorithm showing **every** canonical form and tableau iteration,
and exports the whole working to an output text file.

## Getting started

```bash
git clone https://github.com/tristanels/LPR381_V_9.git
cd LPR381_V_9
```

Open `LPR381/LPR381.slnx` in Visual Studio, or from the command line:

```bash
dotnet build LPR381/LPR381.slnx     # builds solve.exe
dotnet test  LPR381/LPR381.slnx     # runs the unit tests
```

`solve.exe` lands in `LPR381/LPR381/bin/Debug/net10.0/`, with the sample models beside it in `samples/`.

## Structure

| Project | What lives there |
| --- | --- |
| `LPR381.Core` | All the maths. No `Console` calls, so the same code feeds the screen, the output file and the tests. |
| `LPR381` | The console front end (`solve.exe`) and the menu. |
| `LPR381.Tests` | xUnit tests, including the worked examples from the study notes. |

Inside `LPR381.Core`:

- `Models/` — `LinearModel`, `Constraint`, `Variable`, and `Num` (every float comparison goes through `Num`).
- `IO/` — `ModelFileParser` (input file), `TableauFormatter` and `ReportWriter` (screen + output file).
- `Simplex/` — `Tableau`, `PivotEngine`, the pivot rules, `CanonicalFormBuilder`, `PrimalSimplexSolver`.
- `Algorithms/` — `ISolver`, `SolveResult`, `SolverRegistry`, and the placeholder algorithms.
- `Sensitivity/` — `ISensitivityOperation` and the placeholder operations.

## Input file format

```
max +2 +3 +3 +5 +2 +4
+11 +8 +6 +14 +10 +10 <=40
bin bin bin bin bin bin
```

- Line 1: `max` or `min`, then a signed objective coefficient per decision variable.
- Then one line per constraint: signed technological coefficients, the relation (`<=`, `>=`, `=`) and the RHS.
- Last line: one sign restriction per variable — `+`, `-`, `urs`, `int`, `bin`.

Any number of variables and constraints is accepted. `#` and `//` start a comment.

## What is done

- Input file parsing with error messages that name the line and what was expected.
- Canonical form per the study notes: objective moved left, `+s` for `<=`, `-e` then `x -1` for `>=`,
  `=` split into `2'` and `2"`, sign-restriction substitutions, binary bound rows.
- **Primal Simplex**, every table kept and displayed with its pivot column, ratio column, pivot row
  and pivot element, plus a sentence explaining each choice.
- **Dual Simplex**, run automatically first when the canonical form has a negative RHS.
- Special cases: unbounded, infeasible, alternative optima, and a warning when the answer is only
  the relaxed LP of an integer model.
- Output file export containing the model, canonical form, every iteration and the final answer,
  rounded to three decimals.

## Still to build

Each of these is already an `ISolver` in `Algorithms/` (or an `ISensitivityOperation` in `Sensitivity/`)
that appears in the menu and says what is outstanding. Finishing one means filling in `Solve` —
nothing else in the program changes.

- Revised Primal Simplex (product form / price out)
- Branch & Bound Simplex
- Cutting Plane
- Branch & Bound Knapsack
- The twelve sensitivity analysis operations, including duality
- Non-linear bonus criterion

## Team workflow

- `main` is the integration branch — don't commit directly to it.
- Branch per piece of work: `git checkout -b feature/<short-name>`
- Commit, push, and open a pull request into `main`.
- Pull `main` before starting anything new: `git checkout main && git pull`
- Run `dotnet test` before opening a pull request. The tests pin the pivot arithmetic that every
  outstanding algorithm builds on.
