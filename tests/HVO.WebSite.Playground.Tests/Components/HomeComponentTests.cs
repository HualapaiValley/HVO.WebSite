using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using HVO.WebSite.Playground.Components.Pages;

namespace HVO.WebSite.Playground.Tests.Components;

/// <summary>
/// Unit tests for Home Blazor component
/// </summary>
[TestClass]
public class HomeComponentTests : Bunit.TestContext
{
    [TestMethod]
    public void HomeComponent_ShouldRenderCorrectly()
    {
        // Act
        var component = RenderComponent<Home>();

        // Assert
        component.Should().NotBeNull();
        
        // Check main heading
        var heading = component.Find("h1.display-4");
        heading.TextContent.Should().Contain("Welcome to HVO Playground");
        
        // Check lead paragraph
        var leadParagraph = component.Find("p.lead");
        leadParagraph.TextContent.Should().Contain("Test API endpoints with both MVC and Blazor approaches");
    }

    [TestMethod]
    public void HomeComponent_ShouldHaveMvcCard()
    {
        // Act
        var component = RenderComponent<Home>();

        // Assert
        var mvcCard = component.Find("h5.card-title");
        mvcCard.TextContent.Should().Contain("MVC Approach");
        
        var mvcButton = component.Find("a[href='/Home/HealthCheckMVC']");
        mvcButton.Should().NotBeNull();
        mvcButton.TextContent.Should().Contain("Test with MVC");
        mvcButton.ClassList.Should().Contain("btn-primary");
    }

    [TestMethod]
    public void HomeComponent_ShouldHaveBlazorCard()
    {
        // Act
        var component = RenderComponent<Home>();

        // Assert
        var blazorCards = component.FindAll("h5.card-title");
        blazorCards.Should().HaveCountGreaterThan(1);
        
        var blazorCard = blazorCards.FirstOrDefault(c => c.TextContent.Contains("Blazor Approach"));
        blazorCard.Should().NotBeNull();
        
        var blazorButton = component.Find("a[href='/health-check-blazor']");
        blazorButton.Should().NotBeNull();
        blazorButton.TextContent.Should().Contain("Test with Blazor");
        blazorButton.ClassList.Should().Contain("btn-success");
    }

    [TestMethod]
    public void HomeComponent_ShouldHaveFeaturesComparison()
    {
        // Act
        var component = RenderComponent<Home>();

        // Assert
        var featuresCard = component.FindAll("h5.card-title")
            .FirstOrDefault(c => c.TextContent.Contains("Features Comparison"));
        featuresCard.Should().NotBeNull();
        
        // Check for feature lists
        var featureLists = component.FindAll("ul.list-unstyled");
        featureLists.Should().HaveCountGreaterThan(0);
    }

    [TestMethod]
    public void HomeComponent_ShouldHaveResponsiveLayout()
    {
        // Act
        var component = RenderComponent<Home>();

        // Assert
        // Check for Bootstrap responsive classes
        var rows = component.FindAll(".row");
        rows.Should().HaveCountGreaterThan(0);
        
        var columns = component.FindAll("[class*='col-']");
        columns.Should().HaveCountGreaterThan(0);
        
        // Check for responsive columns
        var responsiveColumns = component.FindAll(".col-md-6");
        responsiveColumns.Should().HaveCount(4); // Two main cards + two feature comparison columns
    }
}
