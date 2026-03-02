# Copilot Instructions for HVO.WebSite.Playground

<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

## Project Overview
This is a Blazor Server web application project for testing and demonstrating API endpoints with both Blazor components and traditional MVC views. It serves as a playground for experimenting with .NET 9.0 web technologies and API patterns.

## Technologies Used
- .NET 9.0
- Blazor Server with InteractiveServer render mode
- ASP.NET Core MVC
- API Versioning with URL segments
- Bootstrap 5 for responsive UI
- HttpClient with IHttpClientFactory pattern
- MSTest with WebApplicationFactory-based integration testing

## Architecture Guidelines
- Use Server-side Blazor components for interactive UI with `@rendermode InteractiveServer`
- Implement API versioning with URL segments (e.g., `/api/v1.0/weather`)
- Use IHttpClientFactory for HTTP client management with named clients
- Follow dependency injection patterns throughout the application
- Use proper error handling and structured logging with ILogger
- Implement responsive design with Bootstrap 5
- Support both Blazor and MVC approaches for comparative analysis

## Project-Specific Patterns

### API Controller Structure
- Use `[ApiController]` and `[Route]` attributes
- Implement versioning with `[ApiVersion("1.0")]`
- Return structured JSON responses with consistent format

### Blazor Component Patterns
- Use `@inject` for dependency injection of IHttpClientFactory and ILogger
- Implement loading states and error handling in components
- Use proper state management with `StateHasChanged()`
- Format JSON responses for display with proper indentation
- **Always create code-behind files (.razor.cs) for all Razor components and pages** - Separate markup from logic by creating a corresponding .razor.cs file for every .razor component/page to maintain clean architecture and testability

### MVC Integration
- Support traditional MVC views alongside Blazor components
- Use JavaScript fetch API for client-side API calls in MVC views
- Maintain consistent styling between Blazor and MVC approaches
- Implement proper error handling in both server and client code

## Testing Approach
- Use `WebApplicationFactory<TEntryPoint>` for integration testing
- Exercise both API endpoints and page responses (Blazor + MVC)
- Verify API versioning functionality and negotiated content types
- Cover error handling scenarios and logging expectations
- Reuse MSTest conventions (AAA pattern, `TestMethod` attributes, `ClassInitialize` for expensive setup)

## Documentation
- Project README: `src/HVO.WebSite.Playground/README.md`
- Centralized guides: `docs/projects/website-playground/weather-api-guide.md` for REST walkthroughs

## Code Style Preferences
- Follow HVOv9 standards (no top-level statements, explicit Main method)
- Use C# 12 features where appropriate
- Follow ASP.NET Core conventions and best practices
- Use async/await patterns for all asynchronous operations
- Implement proper disposal patterns for resources
- Use meaningful variable and method names
- XML documentation comments are optional but recommended for complex public APIs
- Use structured logging with appropriate log levels

## Development Workflow
- Port configuration should be flexible (support both 5000 and 5136)
- Support both HTTP and HTTPS in development
- Use dynamic HttpClient configuration to avoid port conflicts
- Include comprehensive error handling and user-friendly error messages
- Maintain clean separation between API, MVC, and Blazor concerns

## UI/UX Guidelines
- Use Bootstrap 5 components and utilities
- Implement responsive design patterns
- Provide clear navigation between different demonstration approaches
- Include loading indicators and error states
- Use consistent styling and branding
- Ensure accessibility with proper ARIA labels and semantic HTML

## API Design
- Follow REST conventions for endpoint structure
- Use consistent JSON response formats
- Include proper HTTP status codes
- Implement comprehensive error responses
- Support content negotiation where appropriate
- Document API endpoints and expected responses
