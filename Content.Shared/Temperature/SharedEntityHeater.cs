// SPDX-FileCopyrightText: 2023 deltanedas <39013340+deltanedas@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 sleepyyapril <123355664+sleepyyapril@users.noreply.github.com>
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Serialization;

namespace Content.Shared.Temperature;

[Serializable, NetSerializable]
public enum EntityHeaterVisuals
{
    Setting
}

/// <summary>
/// What heat the heater is set to, if on at all.
/// </summary>
[Serializable, NetSerializable]
public enum EntityHeaterSetting
{
    Off,
    Low,
    Medium,
    High
}
