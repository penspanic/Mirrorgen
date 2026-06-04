using System;

namespace Mirrorgen;

/// <summary>
/// WGSL backend only: marks an array parameter as a GPU buffer binding rather
/// than a by-value function argument. WGSL has no array-typed function
/// parameters, so a C# method that takes <c>T[]</c> can only be transpiled to
/// a shader function if that array rides a <c>@group/@binding</c> storage
/// buffer. The parameter is dropped from the emitted <c>fn</c> signature and a
/// module-level <c>@group(Group) @binding(Binding) var&lt;storage, read&gt;</c>
/// declaration is emitted in its place; the function body references it by the
/// same name. <c>arr.Length</c> lowers to <c>arrayLength(&amp;arr)</c>.
///
/// <para>The TypeScript backend ignores this attribute entirely — the array
/// stays an ordinary function parameter there — so one C# source can mirror to
/// both targets.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class WgslBufferAttribute : Attribute
{
    /// <summary>Bind group index for the emitted storage-buffer binding.</summary>
    public int Group { get; set; }

    /// <summary>Binding index within <see cref="Group"/>.</summary>
    public int Binding { get; set; }
}
