// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2023 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 DEATHB4DEFEAT <77995199+DEATHB4DEFEAT@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 deltanedas <39013340+deltanedas@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 sleepyyapril <123355664+sleepyyapril@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Shared.Actions;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Ninja.Components;
using Content.Shared.Popups;
using Content.Shared.Examine;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Ninja.Systems;

/// <summary>
/// Handles dashing logic including charge consumption and checking attempt events.
/// </summary>
public sealed class DashAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedChargesSystem _charges = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DashAbilityComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<DashAbilityComponent, DashEvent>(OnDash);
        SubscribeLocalEvent<DashAbilityComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<DashAbilityComponent> ent, ref MapInitEvent args)
    {
        var (uid, comp) = ent;
        _actionContainer.EnsureAction(uid, ref comp.DashActionEntity, comp.DashAction);
        Dirty(uid, comp);
    }

    private void OnGetActions(Entity<DashAbilityComponent> ent, ref GetItemActionsEvent args)
    {
        if (CheckDash(ent, args.User))
            args.AddAction(ent.Comp.DashActionEntity);
    }

    /// <summary>
    /// Handle charges and teleport to a visible location.
    /// </summary>
    private void OnDash(Entity<DashAbilityComponent> ent, ref DashEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var (uid, comp) = ent;
        var user = args.Performer;
        if (!CheckDash(uid, user))
            return;

        if (!_hands.IsHolding(user, uid, out var _))
        {
            _popup.PopupClient(Loc.GetString("dash-ability-not-held", ("item", uid)), user, user);
            return;
        }

        var origin = _transform.GetMapCoordinates(user);
        var target = args.Target.ToMap(EntityManager, _transform);
        if (!_examine.InRangeUnOccluded(origin, target, SharedInteractionSystem.MaxRaycastRange, null))
        {
            // can only dash if the destination is visible on screen
            _popup.PopupClient(Loc.GetString("dash-ability-cant-see", ("item", uid)), user, user);
            return;
        }

        if (!_charges.TryUseCharge(uid))
        {
            _popup.PopupClient(Loc.GetString("dash-ability-no-charges", ("item", uid)), user, user);
            return;
        }

        var xform = Transform(user);
        _transform.SetCoordinates(user, xform, args.Target);
        _transform.AttachToGridOrMap(user, xform);
        args.Handled = true;
    }

    public bool CheckDash(EntityUid uid, EntityUid user)
    {
        var ev = new CheckDashEvent(user);
        RaiseLocalEvent(uid, ref ev);
        return !ev.Cancelled;
    }
}

/// <summary>
/// Raised on the item before adding the dash action and when using the action.
/// </summary>
[ByRefEvent]
public record struct CheckDashEvent(EntityUid User, bool Cancelled = false);
