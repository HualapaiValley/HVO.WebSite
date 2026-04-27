using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class PlaywrightTestSetupTests
{
    [TestMethod]
    [Ignore("Playwright E2E requires browser install and a running target site.")]
    public void PlaywrightSuite_IsConfiguredButDisabledByDefault()
    {
        Assert.Inconclusive("Playwright suite scaffold is configured but intentionally skipped by default.");
    }
}
