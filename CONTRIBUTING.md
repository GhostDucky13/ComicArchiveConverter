# Contributing

Thanks for wanting to help! Bug reports, ideas and pull requests are all welcome.

## Reporting a bug

Open an issue using the **Bug report** template and include:

1. What you did (which buttons/settings).
2. What you expected to happen.
3. What actually happened, including the lines from the **Log** panel.
4. An example of the folder layout (names only, e.g. `Batman/Chapter 1/001.png`).
   **Please don't attach copyrighted comics.**

## Suggesting a feature

Open an issue using the **Feature request** template. Explain the problem you want
solved, not only the solution you have in mind.

## Making a code change

1. Fork the repository and clone your fork.
2. Create a branch: `git switch -c fix/short-description`
3. Open `ComicArchiveConverter.sln` in Visual Studio 2022 (17.8 or newer).
4. Make your change. Keep the GUI thin: logic goes in `ComicArchiveConverter.Core`.
5. Add or update a test in `tests/ComicArchiveConverter.Tests`.
6. Run all tests: **Test > Run All Tests**, or `dotnet test`.
7. Commit with a clear message (see below) and push your branch.
8. Open a pull request describing **what** changed and **why**.

## Code style

- Standard C# naming: `PascalCase` for types and methods, `_camelCase` for private fields.
- Nullable reference types are on; don't silence warnings with `!` without a reason.
- Prefer small methods with names that explain themselves.
- Never block the UI thread: long work runs in `Task.Run` and reports through `IProgress<T>`.

## Commit messages

Use the imperative mood, as if completing "This commit will ...":

```
Add natural sorting for decimal chapter numbers

Chapter 1.5 was sorted after Chapter 10. The comparer now reads
"1.5" as a single number.
```

## Code of conduct

Be respectful and constructive. Harassment of any kind is not tolerated.
