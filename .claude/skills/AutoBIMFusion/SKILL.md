```markdown
# AutoBIMFusion Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches the core development patterns and conventions used in the AutoBIMFusion repository, a C# codebase focused on automating BIM (Building Information Modeling) workflows. You'll learn about file organization, code style, import/export practices, and how to write and run tests. The guide also provides suggested commands for common development workflows.

## Coding Conventions

### File Naming
- Use **PascalCase** for all file names.
  - **Example:** `AutoBIMManager.cs`, `FusionEngine.cs`

### Import Style
- Use **relative imports** within the project.
  - **Example:**
    ```csharp
    using AutoBIMFusion.Models;
    using AutoBIMFusion.Utils;
    ```

### Export Style
- Use **named exports** for classes and methods.
  - **Example:**
    ```csharp
    public class FusionEngine
    {
        public void RunFusion() { ... }
    }
    ```

### Commit Messages
- No strict pattern, but often prefixed with `auto`
- Keep messages concise (average ~25 characters)
  - **Example:** `auto add fusion logic`

## Workflows

### Adding a New Feature
**Trigger:** When implementing new functionality  
**Command:** `/add-feature`

1. Create a new file using PascalCase (e.g., `NewFeature.cs`).
2. Implement your feature as a named class or method.
3. Use relative imports for any dependencies.
4. Write a corresponding test file named `NewFeature.test.cs`.
5. Commit with a message prefixed by `auto` (e.g., `auto add NewFeature`).

### Refactoring Existing Code
**Trigger:** When improving or restructuring code  
**Command:** `/refactor`

1. Identify the code to refactor.
2. Update file and class names to PascalCase if needed.
3. Adjust imports to maintain relative paths.
4. Update or add tests to cover changes.
5. Commit with a descriptive message (e.g., `auto refactor FusionEngine`).

### Writing and Running Tests
**Trigger:** When validating code correctness  
**Command:** `/run-tests`

1. Create test files using the pattern `*.test.cs` (e.g., `FusionEngine.test.cs`).
2. Implement test cases for public methods and classes.
3. Use the project's preferred (unknown) test framework.
4. Run tests using the appropriate test runner for C#.
5. Review and fix any failing tests before committing.

## Testing Patterns

- **Test File Naming:** Use `*.test.cs` for test files.
  - **Example:** `FusionEngine.test.cs`
- **Test Framework:** Not specified; use standard C# testing frameworks (e.g., MSTest, NUnit, xUnit) as appropriate.
- **Test Coverage:** Focus on testing public interfaces and key logic.

## Commands
| Command        | Purpose                                         |
|----------------|-------------------------------------------------|
| /add-feature   | Start the workflow for adding a new feature     |
| /refactor      | Begin a code refactoring workflow               |
| /run-tests     | Run all test suites in the codebase             |
```
