# Contributing Guide

This document outlines the priorities and standards for development to ensure high-quality, consistent, and maintainable outcomes.

---

## Prioritization Framework

### 1. Data
   - Ensure all data is accurate, consistent, and reliable. This is the highest priority. **A feature is only functional if the data it rests on is trustworthy.**

### 2. Functionality
   - Ensure everything works as intended. Functionality takes precedence over all else.
   - **Apply "Keep It Super Simple" Principle**:  
     Keep the implementation as simple as possible without sacrificing functionality. Avoid complex solutions when simpler ones suffice.

### 3. User Experience
   - Focus on usability, flow, and intuitive interactions that make the user journey straightforward and effective.
   - **Maintain Consistency**:  
     Align UX patterns with existing implementations. Avoid introducing entirely new UX concepts or layouts without team discussion and approval to maintain a consistent experience across the application.
   - **User Design Requests as Advisory**:  
     Treat user or stakeholder design requests as guidance, not requirements. Prioritize consistency and maintainability over custom requests that could introduce inconsistencies in the established UX.

### 4. Performance and Stability
   - Guarantee smooth, high-performing features that avoid lags or bottlenecks.
   - **Transactional Integrity**:  
     Handle database and other critical transactions with a "bank-grade" approach – they must either complete successfully or automatically retry without compromising data integrity.
   - **Resiliency and Fault Tolerance**:  
     Assume the server or external services might fail at any time. Ensure the application can handle such failures gracefully, with retries and fallbacks as needed, so that a single point of failure does not compromise the application's stability.
   - **Integrate Logging, Tracing, and Metrics**:  
     Include logging, tracing, and metrics for critical operations as part of the code, ensuring the application's behavior is trackable and performance can be monitored effectively.
   - **Apply "Don't Repeat Yourself" Principle**:  
     Avoid redundant code. Ensure that repeated logic is extracted to shared functions or components to minimize maintenance overhead.
   - **Apply "[SOLID](https://docs.microsoft.com/en-us/archive/msdn-magazine/2014/may/csharp-best-practices-dangers-of-violating-solid-principles-in-csharp)" Principles**:  
     Strengthens component modularity, adaptability, and reliability, enhancing the system's stability under changing conditions.
   - **Leverage [Traditional](https://refactoring.guru/design-patterns/catalog) and [Cloud](https://docs.microsoft.com/en-us/azure/architecture/patterns/) Design Patterns**:  
     Use established patterns to create consistent, reusable, and scalable solutions, enhancing system stability and simplifying complex problem-solving.

### 5. User Interface
   - Keep the UI clean, simple, and aligned with our established design standards. Avoid eye-catching embellishments, unnecessary styling, or custom designs that deviate from standard practice.

---

## UI Implementation Standards

- **Use FluentUI Blazor Exclusively**:  
  FluentUI Blazor is the sole framework for all UI components and styling. Avoid integrating other UI libraries or frameworks.
  
- **Design References**:  
  For UI inspiration, refer to [FluentUI Blazor](https://www.fluentui-blazor.net/)'s site first. If additional ideas are needed, consult the [Azure Portal](https://portal.azure.com/), [Outlook.com](https://outlook.com/), or general [Fluent Design](https://fluent2.microsoft.design/) guidelines.

---

## Custom Implementations

- **Avoid Customization**:  
  Refrain from custom CSS, JavaScript, images, emojis, fonts, browser-specific functions, non-FluentUI controls, or non-Microsoft libraries unless absolutely necessary.
  
- **Use Case Exceptions**:  
  Custom implementations may be required in unique cases, such as applying a specific color where a component lacks options, or using a custom library for a specialized function. However, customization should be the last resort and require a team discussion beforehand.
  
- **Apply "You Ain't Gonna Need It" Principal**:  
  Only implement features or customizations if they are genuinely necessary and currently required. Avoid speculative additions that add complexity without immediate benefit.

---

## Staying Updated

- **Regularly Check FluentUI Blazor Updates**:  
  Make it a habit to review [FluentUI Blazor's "What's New"](https://www.fluentui-blazor.net/WhatsNew) section to stay informed of changes or additions.

- **Consult Microsoft Documentation First**:  
  Microsoft's official .NET documentation should be your primary source for development guidance.

- **Verify AI-Assisted Code**:  
  AI tools like Claude, Gemini, ChatGPT, or Grok can provide valuable suggestions, but don't rely on them entirely. Always review and test any AI-generated code line-by-line to ensure accuracy and reliability.

---

## Source Control Guidelines

1. **Code for Others, Not Just Yourself**:  
   Write clear and readable code for other developers, not just yourself.

    > _Any fool can write code that a computer can understand._  
    > _Good programmers write code that humans can understand._  
    > \- Martin Fowler

2. **Quality Over Quick Completion**:  
   Aim to implement tasks correctly, not just to get them done, in other words, avoid "quick and dirty" implementations.

3. **Review Before Commit**:  
   Double-check changes before committing to ensure they include only intentional, relevant modifications.

4. **Utilize `.editorconfig` and IDE Extensions**: 
   Follow the project's `.editorconfig` settings for consistent styling and formatting. Make sure you have the supporting extension installed in your IDE.

5. **Adhere to Microsoft's Coding Conventions**  
   Follow Microsoft's official C# coding conventions as detailed in  
   https://learn.microsoft.com/en-us/previous-versions/mixed-reality/world-locking-tools/Documentation/HowTos/codingconventions and  
   https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions.  
   Any deviation should be discussed and agreed upon.

6. **Purpose-Driven Changes Only**:  
   Each PR should contain only code related to a specific feature, bug fix, or adjustment. Unrelated changes should go in separate PRs for clarity.

7. **Avoid Unrelated Whitespace and Minor Style Edits**:  
   Avoid whitespace-only edits. If formatting needs cleanup, make it a separate "cleanup" commit rather than mixing it with feature or bug fix changes.

8. **Preserve Key Comments**:  
   Don't remove or alter important comments, such as license notes, key decisions, explanations that aid in understanding the code, or instructions for the compiler.

9. **Include Issue References**:  
   Link relevant issues in comments for context using this format: 
  
    ```csharp
    // TODO ISSUE https://<link> <description>
    ```

10. **Avoid Abbreviations in Naming**:  
   Use self-explanatory, full-word variable names. Future developers may not share the same domain knowledge.
    - For MSSQL, use PascalCase for all database objects with singular, fully descriptive names (e.g., `Customer` instead of `Customers`), avoiding abbreviations, reserved keywords, special characters, spaces, and unnecessary prefixes.

11. **Avoid Redundant Comments**:  
    Comment only when necessary to explain business logic, methods attempted, or reasoning behind specific implementations. Don't restate the code:

    ```csharp
    // converts to a list 
    var cacheKeys = cacheKeys.ToList();
    ```

12. **Separate Issues from PRs**:  
    An issue explains what needs to be done, while a PR shows what has been done. Keep each distinct in communication.

---

## Local Testing and Deployment Readiness

- **Verify Locally Before Committing**:  
  Ensure the entire codebase runs as expected locally using Docker-Compose or Minikube before pushing any updates to the integration environment.

- **Progressive Testing Approach**:  
  After local verification, deploy to the integration environment for further validation. Only proceed to the testing environment after confirming stability and functionality.

---

## Issue & Pull Request (PR) Workflow

### Separation of Contexts

- The **Issue** section is reserved for requesters to outline requirements or describe problems and should remain free of technical discussions.  
- Technical implementation details and discussions should be confined to the **Pull Request (PR)** section. 

### Issue Ownership

- Issues remain the responsibility of the assigned developer until officially handed off. _(Asking a quick question does not qualify as a handoff.)_ Acceptable handoff scenarios include:  
  - **To another developer** for continued work.  
  - **To QA** for testing against functional requirements and acceptance criteria.
  - **To Business Users** for User Acceptance Testing.
  - **To the product owner** for further review or decision-making.  

### PR Reviewer Responsibilities

- PR reviews must always be conducted by developers other than the assignee.  
- Reviewers are not expected to conduct functional testing of the PR.  
- Reviewers should avoid merging the PR themselves, to maintain separation of duties and alignment with deployment schedules.  

### PR Assignee and Merging

- The PR assignee is responsible for merging the PR after review approval.  
- After merging, the PR assignee must perform developer testing of the changes to ensure their integrity.  

---

By following this guide, you contribute to a consistent, functional, and maintainable project. Thanks for your commitment to quality!

  > _Always leave the code better than you found it!_
