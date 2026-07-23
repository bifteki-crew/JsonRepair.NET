using System.Text.Json;
using FluentAssertions;
using JsonRepair.Serialization;
using Xunit;

namespace JsonRepair.Tests;

public class ConverterTests
{
    public record BiftekiCrewDto(string CrewMember, bool FlameGrilled, string? SecretIngredient, int Orders);

    [Fact]
    public void JsonRepairEngine_Deserialize_ShouldDirectlyDeserializeMalformedJson()
    {
        // Arrange malformed LLM JSON with single quotes, unquoted keys, Python True/None, and trailing comma
        string malformed = """
        {
            crewMember: 'Chef Bifteki',
            flameGrilled: True,
            secretIngredient: None,
            orders: 42,
        }
        """;

        // Act
        var result = JsonRepairEngine.Deserialize<BiftekiCrewDto>(malformed);

        // Assert
        result.Should().NotBeNull();
        result!.CrewMember.Should().Be("Chef Bifteki");
        result.FlameGrilled.Should().BeTrue();
        result.SecretIngredient.Should().BeNull();
        result.Orders.Should().Be(42);
    }

    [Fact]
    public void JsonRepairEngine_TryDeserialize_ShouldReturnTrue_ForValidMalformedJson()
    {
        // Arrange
        string malformed = "{ user: 'Bifteki', rating: 5, }";

        // Act
        bool success = JsonRepairEngine.TryDeserialize<BiftekiUser>(malformed, out var user);

        // Assert
        success.Should().BeTrue();
        user.Should().NotBeNull();
        user!.User.Should().Be("Bifteki");
        user.Rating.Should().Be(5);
    }

    public record BiftekiUser(string User, int Rating);
}
