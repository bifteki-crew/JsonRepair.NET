using System.Text.Json;
using FluentAssertions;
using JsonRepair.Tests.TestCases;
using Xunit;

namespace JsonRepair.Tests;

public class RepairEngineTests
{
    [Theory]
    [ClassData(typeof(JsonEdgeCaseData))]
    public void Repair_ShouldProduceValidJson_ForKnownEdgeCases(string testId, string input, string expected)
    {
        // Act
        string repaired = JsonRepairEngine.Repair(input);

        // Assert: Ensure repaired JSON parses successfully
        repaired.Should().NotBeNullOrWhiteSpace();
        using var repairedDoc = JsonDocument.Parse(repaired);
        using var expectedDoc = JsonDocument.Parse(expected);

        // Assert semantic equality via JsonElement
        JsonElement.DeepEquals(repairedDoc.RootElement, expectedDoc.RootElement)
            .Should().BeTrue(because: $"Test case {testId} should produce semantically identical JSON to expected output. Repaired: '{repaired}'");
    }

    [Fact]
    public void TryParse_ShouldReturnTrueAndValidDocument_ForMalformedJson()
    {
        // Arrange
        string malformed = "```json\n{'user': 'Alice', 'role': None, 'permissions': ['read', 'write',]}\n```";

        // Act
        bool success = JsonRepairEngine.TryParse(malformed, out var doc);

        // Assert
        success.Should().BeTrue();
        doc.Should().NotBeNull();
        doc!.RootElement.GetProperty("user").GetString().Should().Be("Alice");
        doc.RootElement.GetProperty("role").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("permissions").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Repair_ShouldRespectNormalizeQuotesOption()
    {
        // Arrange
        var options = new JsonRepairOptions { NormalizeQuotes = false };
        string malformed = "{'item': 'Bifteki'}";

        // Act
        string repaired = JsonRepairEngine.Repair(malformed, options);

        // Assert
        repaired.Should().Be("{'item':'Bifteki'}");
    }

    [Fact]
    public void TryDeserialize_ShouldReturnTrue_WhenResultIsNull()
    {
        // Act
        bool successString = JsonRepairEngine.TryDeserialize<string>("null", out var stringResult);
        bool successNullable = JsonRepairEngine.TryDeserialize<int?>("null", out var intResult);

        // Assert
        successString.Should().BeTrue();
        stringResult.Should().BeNull();
        successNullable.Should().BeTrue();
        intResult.Should().BeNull();
    }

    [Fact]
    public void Repair_ShouldNotConvertLiteralPrefixInsideLongerWord()
    {
        // Arrange: "TrueStuff" starts with "True" but is one identifier.
        // Unquoted string VALUES are not yet supported (Tier 3 / 0.3.0),
        // so the identifier must pass through untouched instead of being
        // corrupted into {"a":true,"Stuff"}.
        string malformed = "{a: TrueStuff}";

        // Act
        string repaired = JsonRepairEngine.Repair(malformed);

        // Assert
        repaired.Should().Be("{\"a\":TrueStuff}");
    }
}
