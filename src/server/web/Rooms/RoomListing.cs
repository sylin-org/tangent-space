namespace TangentSpace.Rooms;

public sealed record RoomListing(IReadOnlyList<RoomDescription> Rooms, int Page, int? NextPage, DateTimeOffset CheckedAt);
