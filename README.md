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

Golden Section Search is the exception: it works on a curve, not a table, so it shares none of this
(see below).

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

### Non-linear input format (bonus criterion)

A non-linear model is written a different, shorter way — a function of one variable and the interval
to search it over:

```
max 4sin(x)(1 + cos(x))
0 <= x <= 1.5707963268
tolerance 0.05
```

Line 3 is optional and defaults to `0.001`. You may write `+ - * / ^`, brackets, the functions
`sin cos tan sqrt exp ln log abs` and the constants `pi` and `e`. Multiplication may be left implicit,
so `4sin(x)(1 + cos(x))` and even the notes' `4sinx(1 + cosx)` both parse.

The program tells the two formats apart on its own: a linear objective line is nothing but signed
numbers, so a letter on that line means the file is non-linear.

## Algorithms

| Menu | Algorithm | Notes section |
| --- | --- | --- |
| 1 | Primal Simplex (Dual Simplex first when the RHS is negative) | §1, §2, §3 |
| 2 | Revised Primal Simplex — B⁻¹, CBV·B⁻¹, price out, product form, every iteration | — |
| 3 | Branch & Bound Simplex — depth-first with backtracking, all sub-problems, fathoming, best candidate | §4 |
| 4 | Cutting Plane — Gomory cuts, fraction split shown per cut | §5 |
| 5 | Branch & Bound Knapsack — z/c ranking, fill, left/needed fraction, forced branches | §12 |
| — | Golden Section Search — for non-linear models; chosen automatically, since it is the only one that fits | §18 |

**Bonus criterion — the non-linear problem.** Load a file in the non-linear format and option 3 goes
straight to the Golden Section Search; there is no algorithm menu, because none of the tableau methods
apply to a curve. It places two interior points using the golden ratio conjugate `0.618…`, compares
`f` at them, throws away the end that cannot hold the optimum, and repeats until the interval is
narrower than the tolerance. It never differentiates `f` and never builds a table, which is why it
handles `4sin(x)(1 + cos(x))`.

`golden.txt` is the worked example from §18 of the notes and reproduces it exactly, ending at
`x = 1.041625`, `f = 5.195991` after 8 iterations.

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

`equality.txt`, `integer.txt`, `knapsack.txt` (the brief's example), `minimisation.txt`, `santa.txt`
(the LP from the module's Excel workbook), `unbounded.txt`, `infeasible.txt`, and four `broken_*.txt`
files that each trip a different parser error.

Non-linear: `golden.txt` (the §18 worked example), `golden_quadratic.txt` (an interior maximum) and
`golden_minimum.txt` (the same search minimising, so the comparison flips).

## Team workflow

- `main` is the integration branch.
- Branch per piece of work: `git checkout -b feature/<short-name>`
- Commit, push, and open a pull request into `main`.
- Pull `main` before starting anything new: `git checkout main && git pull`
- Run `dotnet test` before opening a pull request. The tests pin the pivot arithmetic that every
  algorithm builds on, and check the worked examples from the study notes cell by cell.
