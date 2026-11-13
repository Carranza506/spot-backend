using System.Text.Json.Serialization;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.DTOs;

/// <summary>
/// Serializes <see cref="ContactType"/> as its contract name ("WHATSAPP"). Reading is
/// case-insensitive (System.Text.Json's default for enum names), but numeric values are rejected,
/// so a body can only ever carry one of the ContactType enum names from contracts/spot-api.yaml.
/// </summary>
public sealed class ContactTypeJsonConverter() : JsonStringEnumConverter<ContactType>(namingPolicy: null, allowIntegerValues: false);
