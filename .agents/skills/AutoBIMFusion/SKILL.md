```markdown
# AutoBIMFusion Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches the core development patterns and conventions used in the AutoBIMFusion repository, a C# codebase focused on automating BIM (Building Information Modeling) workflows. You'll learn how to structure files, write imports/exports, follow commit message patterns, and understand the project's approach to testing.

## Coding Conventions

### File Naming
- **Convention:** PascalCase for all file names.
  - **Example:** `AutoBIMManager.cs`, `FusionEngine.cs`

### Import Style
- **Convention:** Use relative imports within the project.
  - **Example:**
    ```csharp
    using AutoBIMFusion.Models;
    using AutoBIMFusion.Utils;
    ```

### Export Style
- **Convention:** Use named exports for classes and methods.
  - **Example:**
    ```csharp
    public class FusionEngine
    {
        public void RunFusion() { ... }
    }
    ```

### Commit Messages
- **Style:** Freeform, with no strict prefixes, average length ~53 characters.
  - **Example:**  
    ```
    Add support for new BIM element types in FusionEngine
    ```

## Workflows

### Add a New BIM Automation Feature
**Trigger:** When you need to introduce a new automation capability.
**Command:** `/add-feature`

1. Create a new C# file using PascalCase (e.g., `NewFeature.cs`).
2. Implement your feature as a public class with named exports.
3. Use relative imports to reference existing models or utilities.
4. Write a freeform commit message describing your change.
5. Add or update test files as needed (see Testing Patterns).

### Refactor Existing Code
**Trigger:** When improving or restructuring code for clarity or performance.
**Command:** `/refactor`

1. Identify the target files and ensure file names follow PascalCase.
2. Update imports to use relative paths if necessary.
3. Refactor classes/methods using named exports.
4. Write a clear, concise commit message about the refactor.
5. Run or update tests to ensure no regressions.

### Add or Update Tests
**Trigger:** When adding new features or fixing bugs.
**Command:** `/add-test`

1. Create or update test files matching the pattern `*.test.*` (e.g., `FusionEngine.test.cs`).
2. Write test cases for new or modified functionality.
3. Use the project's (unknown) testing framework conventions.
4. Commit with a message describing the test addition or update.

## Testing Patterns

- **Test File Pattern:** Files are named with the pattern `*.test.*` (e.g., `FusionEngine.test.cs`).
- **Framework:** Not explicitly detected; follow existing test file examples.
- **Best Practice:** Add or update tests whenever you add new features or refactor code.

## Commands
| Command      | Purpose                                        |
|--------------|------------------------------------------------|
| /add-feature | Add a new BIM automation feature               |
| /refactor    | Refactor existing code for clarity/performance |
| /add-test    | Add or update tests for new/existing features  |
```