# LPR381 Project

Group project for LPR381 (Linear Programming). A menu-driven `solve.exe` that reads an LP/IP from a
text file, solves it with a chosen algorithm showing **every** canonical form and tableau iteration,
runs sensitivity analysis and duality on the answer, and exports the whole working to a text file.

## Getting started

```bash
git clone https://github.com/tristanels/LPR381_V_9.git
cd LPR381_V_9
```

Open `LPR381/LPR381.slnx` in Visual Studio (set **LPR381** as the startup project), or from the
command line:

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
- `IO/` — `ModelFileParser` (input file), `TableauFormatter`, `MatrixFormatter`, `ReportWriter`.
- `Simplex/` — `Tableau`, `PivotEngine`, the pivot rules, `SimplexEngine`, `CanonicalFormBuilder`,
  `TableauOperations`, `BasisMatrix`, `IntegerRules`, `PrimalSimplexSolver`.
- `Algorithms/` — `ISolver`, `SolveResult`, `SolverRegistry`, and the five other algorithms.
- `Sensitivity/` — `ISensitivityOperation`, `SensitivityContext`, and the twelve operations.

### How the pieces are shared

Nothing is implemented twice. `PivotEngine.Pivot` is the only place a table is ever transformed
(study notes §2), and `SimplexEngine` is the only place a table is driven to optimality — Dual
Simplex first when the RHS is negative, then Primal. Everything else feeds tables into it:

| Algorithm | What it adds on top |
| --- | --- |
| Primal Simplex | canonical form → `SimplexEngine` |
| Revised Primal Simplex | same canonical form, worked through `BasisMatrix` (B⁻¹, price out, product form) |
| Branch & Bound Simplex | `TableauOperations.AddConstraint` for each bound, then `SimplexEngine` per node |
| Cutting Plane | `TableauOperations.AddConstraint` for each Gomory cut, then `SimplexEngine` |
| Sensitivity "apply a change" | edits one cell of the optimal table, then `SimplexEngine` |
| Non-Linear | rewrites the model as an LP, then hands it to `PrimalSimplexSolver` |

Only Knapsack B&B stands apart, because the notes teach it as a ranking algorithm rather than a
tableau one.

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

**Non-linear (bonus criterion):** an objective coefficient may carry a power, so `max +1^2 +3` means
`max z = x1² + 3x2`. Those models go to the Non-Linear solver.

## Algorithms

| Menu | Algorithm | Notes section |
| --- | --- | --- |
| 1 | Primal Simplex (Dual Simplex first when the RHS is negative) | §1, §2, §3 |
| 2 | Revised Primal Simplex — B⁻¹, CBV·B⁻¹, price out, product form, every iteration | — |
| 3 | Branch & Bound Simplex — depth-first with backtracking, all sub-problems, fathoming, best candidate | §4 |
| 4 | Cutting Plane — Gomory cuts, fraction split shown per cut | §5 |
| 5 | Branch & Bound Knapsack — z/c ranking, fill, left/needed fraction, forced branches | §12 |
| 6 | Non-Linear (separable) — piecewise linear approximation solved by the Primal Simplex | bonus |

**Bonus criterion — "any non-linear problem, with any algorithm".** Loading a model with a `^` power
unlocks *every* algorithm in the menu, not just option 6. The lambda method rewrites the curve as an
ordinary LP first, so whichever algorithm you pick is the one that actually solves it. On
`nonlinear_integer.txt` that is Primal, Revised, Branch & Bound and Cutting Plane — five of the six
(Knapsack B&B still needs a pure binary knapsack, and says so). When the non-linear variable is `int`,
the breakpoints sit on whole numbers and the weights become binary, so the answer is *exact* rather
than approximated.

Special cases handled: unbounded, infeasible, alternative optima, cycling, and a warning when an
answer is only the relaxed LP of an integer model. Each algorithm refuses a model it does not suit
and says why.

## Sensitivity analysis

All twelve operations from the brief, run on the optimal table from the Primal or Revised Simplex:
ranges and changes for non-basic variables, basic variables, RHS values and coefficients inside
non-basic columns; add a new activity; add a new constraint; shadow prices; and duality (build the
dual, solve it, report the duality gap and whether duality is strong or weak).

Each analysis is kept and included in the exported output file.

## Samples

`equality.txt`, `integer.txt`, `knapsack.txt` (the brief's example), `minimisation.txt`, `nonlinear.txt`,
`nonlinear_integer.txt`, `santa.txt` (the LP from the module's Excel workbook), `unbounded.txt`,
`infeasible.txt`, and four `broken_*.txt` files that each trip a different parser error.

## Team workflow

- `main` is the integration branch.
- Branch per piece of work: `git checkout -b feature/<short-name>`
- Commit, push, and open a pull request into `main`.
- Pull `main` before starting anything new: `git checkout main && git pull`
- Run `dotnet test` before opening a pull request. The tests pin the pivot arithmetic that every
  algorithm builds on, and check the worked examples from the study notes cell by cell.
