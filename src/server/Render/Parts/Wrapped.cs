namespace Tangent.Render;

/// <summary>The place's roles as whoever may change them finds them, with the forms that create, change and delete them.</summary>
public sealed record RoleList(Tangent.Access.RolesPage Page) : IPart;

/// <summary>The place's appearance and its words, with the form that changes them and its preview.</summary>
public sealed record AppearanceForm(Tangent.Space.AppearanceSettingsPage Page) : IPart;

/// <summary>A participant's own account and where each of their articles stands there.</summary>
public sealed record ConnectionList(Tangent.Connections.ConnectionsPage Page) : IPart;

/// <summary>Where someone signs in with their handle, or on another server, and why the last try did not take.</summary>
public sealed record SignInForm(Tangent.Participants.SignIn Page) : IPart;

/// <summary>How the place is set up at first run, and the pointers its site carries once it is.</summary>
public sealed record SetUp(Tangent.Space.FirstRun Page) : IPart;

/// <summary>The form that creates a Tangent, or why the reader may not.</summary>
public sealed record NewTangentForm(Tangent.Tangents.NewTangentPage Page) : IPart;

/// <summary>A Tangent's settings: the form that renames, describes and orders it, sets its kind and audience, and its tabs.</summary>
public sealed record TangentSettingsForm(Tangent.Tangents.TangentSettingsPage Page) : IPart;

/// <summary>Which roles read and take part in a Tangent, with the form that changes it.</summary>
public sealed record TangentAccessForm(Tangent.Tangents.TangentAccessPage Page) : IPart;

/// <summary>Who comes into a private Tangent, how, and who asks, with what its manager may decide.</summary>
public sealed record TangentPeopleList(Tangent.Tangents.TangentPeoplePage Page) : IPart;

/// <summary>A report of a Post: the Post quoted, and the form that sends it to the moderators, or what was received.</summary>
public sealed record ReportPanel(Tangent.Posts.ReportPage Page) : IPart;
