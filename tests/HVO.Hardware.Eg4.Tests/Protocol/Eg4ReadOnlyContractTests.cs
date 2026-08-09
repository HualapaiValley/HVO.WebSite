using FluentAssertions;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Tests.Fixtures;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg4ReadOnlyContractTests
{
    [TestMethod]
    public void TransportSurface_IsReadOnlyAndContainsNoModelRegisterConstants()
    {
        typeof(IEg4RegisterTransport).GetMethods().Select(method => method.Name)
            .Should().Equal("ReadRegistersAsync");
        typeof(IEg4RegisterTransport).Assembly.GetTypes()
            .SelectMany(type => type.GetMembers())
            .Should().NotContain(member => member.Name.Contains("WriteRegister", StringComparison.OrdinalIgnoreCase));
        typeof(IEg46500ExInquiryTransport).GetMethods().Select(method => method.Name)
            .Should().Equal("ExchangeAsync");
        typeof(IEg46500ExInquiryTransport).Should().Implement<IAsyncDisposable>();
        typeof(IEg46500ExInquiryTransport).GetMethod("ExchangeAsync")!.GetParameters()[0].ParameterType
            .Should().Be<Eg46500ExInquiry>();
        Enum.GetNames<Eg46500ExInquiry>().Should().OnlyContain(name =>
            !name.Contains("Write", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Set", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FixtureBuilder_UsesOnlyCallerProvidedAddressAndValues()
    {
        var (request, response) = new Eg4RegisterFixtureBuilder(321)
            .Value(0, 10).Value(2, 30).Build(7, Eg4RegisterTable.Input);

        request.Should().Be(new Eg4ReadRegistersRequest(7, Eg4RegisterTable.Input, 321, 3));
        response.Registers.Should().Equal(10, 0, 30);
    }

    [TestMethod]
    public void ReadRequest_RejectsAddressOverflow()
    {
        var valid = new Eg4ReadRegistersRequest(1, Eg4RegisterTable.Holding, ushort.MaxValue, 1);
        Action overflow = () => new Eg4ReadRegistersRequest(1, Eg4RegisterTable.Holding, ushort.MaxValue, 2);

        valid.RegisterCount.Should().Be(1);
        overflow.Should().Throw<ArgumentOutOfRangeException>();
    }
}
