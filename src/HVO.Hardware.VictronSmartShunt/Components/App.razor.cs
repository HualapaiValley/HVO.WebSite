using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.VictronSmartShunt.Components;

public partial class App
{
    [Inject] private IWebHostEnvironment Environment { get; set; } = default!;

    private string AppCssVersion
    {
        get
        {
            var fileInfo = Environment.WebRootFileProvider.GetFileInfo("app.css");
            if (!fileInfo.Exists || fileInfo.LastModified == DateTimeOffset.MinValue)
                return "0";

            return fileInfo.LastModified.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }
    }
}
