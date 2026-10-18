using System.Text.Json.Serialization;
using Spot.Booking.Api.Models;

namespace Spot.Booking.Api.DTOs;

/// <summary>
/// Writes <see cref="BookingStatus"/> as the contract's uppercase names ("PENDING", …) instead of
/// numbers. Same approach as Spot.Business.Api's ContactTypeJsonConverter.
/// </summary>
public sealed class BookingStatusJsonConverter() : JsonStringEnumConverter<BookingStatus>(namingPolicy: null, allowIntegerValues: false);
