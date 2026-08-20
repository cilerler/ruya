// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "TestHost is part of the published testing-library API.", Scope = "type", Target = "~T:" + nameof(Ruya) + "." + nameof(Ruya.Testing) + "." + nameof(Ruya.Testing.Primitives) + "." + nameof(Ruya.Testing.Primitives.TestHost))]
[assembly: SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "TestBase is part of the published testing-library API.", Scope = "type", Target = "~T:" + nameof(Ruya) + "." + nameof(Ruya.Testing) + "." + nameof(Ruya.Testing.Primitives) + "." + nameof(Ruya.Testing.Primitives.TestBase<object>) + "`1")]
