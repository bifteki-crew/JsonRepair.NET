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
}
