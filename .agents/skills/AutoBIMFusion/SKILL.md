```markdown
# AutoBIMFusion Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches you the core development patterns and conventions used in the AutoBIMFusion repository, a C# codebase with no detected framework. You'll learn how to structure files, write imports and exports, follow commit conventions, and understand the testing approach. This guide also provides suggested commands for common workflows.

## Coding Conventions

### File Naming
- Use **PascalCase** for all file names.
  - **Example:** `ProjectManager.cs`, `AutoBIMFusionCore.cs`

### Import Style
- Use **relative imports**.
  - **Example:**
    ```csharp
    using AutoBIMFusion.Models;
    using AutoBIMFusion.Utils;
    ```

### Export Style
- Use **named exports** (public classes, methods, etc.).
  - **Example:**
    ```csharp
    public class ProjectManager
    {
        public void CreateProject(string name) { ... }
    }
    ```

### Commit Patterns
- Commit types are mixed, but often use the `fix` prefix for bug fixes.
- Average commit message length: 64 characters.
  - **Example:**  
    ```
    fix: resolve null reference in ProjectManager initialization
    ```

## Workflows

### Code Contribution
**Trigger:** When adding or updating features or fixing bugs  
**Command:** `/contribute`

1. Create a new branch for your changes.
2. Follow PascalCase for file names and use relative imports.
3. Export classes and methods using named exports.
4. Write clear commit messages, prefixed with `fix` for bug fixes when appropriate.
5. Submit a pull request for review.

### Testing Code
**Trigger:** When verifying new or changed functionality  
**Command:** `/test`

1. Create or update test files following the `*.test.*` pattern.
2. Use the project's preferred (undetected) testing framework.
3. Run all tests to ensure correctness.
4. Address any failing tests before merging.

## Testing Patterns

- Test files follow the `*.test.*` naming convention.
  - **Example:** `ProjectManager.test.cs`
- The specific testing framework is not detected; use the project's existing patterns.
- Place tests alongside or in a dedicated test directory as per repository structure.

## Commands
| Command      | Purpose                                      |
|--------------|----------------------------------------------|
| /contribute  | Start the code contribution workflow         |
| /test        | Run or write tests for your code changes     |
```
