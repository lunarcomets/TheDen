// SPDX-FileCopyrightText: 2024 sleepyyapril <flyingkarii@gmail.com>
// SPDX-FileCopyrightText: 2025 Solaris <60526456+SolarisBirb@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 VMSolidus <evilexecutive@gmail.com>
// SPDX-FileCopyrightText: 2025 sleepyyapril <123355664+sleepyyapril@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Server.AlertLevel;
using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.DoAfter;
using Content.Server.Examine;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Lightning;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server.Singularity.Components;
using Content.Server.Singularity.EntitySystems;
using Content.Server.Traits.Assorted;
using Content.Shared._EE.CCVars;
using Content.Shared._EE.Supermatter.Components;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.DeviceLinking;

namespace Content.Server._EE.Supermatter.Systems;

public sealed partial class SupermatterSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly ExamineSystem _examine = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly GravityWellSystem _gravityWell = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly ParacusiaSystem _paracusia = default!;
    [Dependency] private readonly PointLightSystem _light = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _link = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupermatterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SupermatterComponent, AtmosDeviceUpdateEvent>(OnSupermatterUpdated);

        SubscribeLocalEvent<SupermatterComponent, StartCollideEvent>(OnCollideEvent);
        // SubscribeLocalEvent<SupermatterComponent, EmbeddedEvent>(OnEmbedded); // -- requires EE's throwing system
        SubscribeLocalEvent<SupermatterComponent, InteractHandEvent>(OnHandInteract);
        SubscribeLocalEvent<SupermatterComponent, InteractUsingEvent>(OnItemInteract);
        SubscribeLocalEvent<SupermatterComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SupermatterComponent, SupermatterDoAfterEvent>(OnGetSliver);
        SubscribeLocalEvent<SupermatterComponent, GravPulseEvent>(OnGravPulse);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityManager.EntityQueryEnumerator<SupermatterComponent>();
        while (query.MoveNext(out var uid, out var sm))
            AnnounceCoreDamage(uid, sm);
    }

    private void OnMapInit(EntityUid uid, SupermatterComponent sm, MapInitEvent args)
    {
        // Set the yell timer
        sm.YellTimer = TimeSpan.FromSeconds(_config.GetCVar(ECCVars.SupermatterYellTimer));

        // Set the sound
        _ambient.SetAmbience(uid, true);

        // Add air to the initialized SM in the map so it doesn't delam on its own
        var mix = _atmosphere.GetContainingMixture(uid, true, true);
        mix?.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard - mix.GetMoles(Gas.Oxygen));
        mix?.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard - mix.GetMoles(Gas.Nitrogen));


        // Send the inactive port for any linked devices
        if (HasComp<DeviceLinkSourceComponent>(uid))
            _link.InvokePort(uid, sm.PortInactive);
    }

    public void OnSupermatterUpdated(EntityUid uid, SupermatterComponent sm, AtmosDeviceUpdateEvent args)
    {
        ProcessAtmos(uid, sm);
        HandleDamage(uid, sm);

        if (sm.Damage >= sm.DamageDelaminationPoint || sm.Delamming)
            HandleDelamination(uid, sm);

        HandleLight(uid, sm);
        HandleVision(uid, sm);
        HandleStatus(uid, sm);
        HandleSoundLoop(uid, sm);
        HandleAccent(uid, sm);

        if (sm.Power > _config.GetCVar(ECCVars.SupermatterPowerPenaltyThreshold) || sm.Damage > sm.DamagePenaltyPoint)
        {
            SupermatterZap(uid, sm);
            GenerateAnomalies(uid, sm);
        }
    }

    private void OnCollideEvent(EntityUid uid, SupermatterComponent sm, ref StartCollideEvent args)
    {
        TryCollision(uid, sm, args.OtherEntity, args.OtherBody);
    }

    // requires EE's throwing system/embed edits and i truly cannot be bothered to also go port those
    /*private void OnEmbedded(EntityUid uid, SupermatterComponent sm, ref EmbeddedEvent args)
    {
        TryCollision(uid, sm, args.Embedded, checkStatic: false);
    }*/

    private void OnHandInteract(EntityUid uid, SupermatterComponent sm, ref InteractHandEvent args)
    {
        var target = args.User;

        if (HasComp<SupermatterImmuneComponent>(target) || HasComp<GodmodeComponent>(target))
            return;

        if (!sm.HasBeenPowered)
            LogFirstPower(uid, sm, target);

        var power = 200f;
        if (TryComp<PhysicsComponent>(target, out var physics))
            power += physics.Mass;

        sm.MatterPower += power;

        _popup.PopupEntity(Loc.GetString("supermatter-collide-mob", ("sm", uid), ("target", target)), uid, PopupType.LargeCaution);
        _audio.PlayPvs(sm.DustSound, uid);

        // Prevent spam or excess power production
        AddComp<SupermatterImmuneComponent>(target);

        _chatManager.SendAdminAlert($"{EntityManager.ToPrettyString(uid):uid} has consumed {EntityManager.ToPrettyString(target):target}");
        _adminLog.Add(LogType.EntityDelete, LogImpact.High, $"{EntityManager.ToPrettyString(target):target} touched {EntityManager.ToPrettyString(uid):uid} and was destroyed at {Transform(uid).Coordinates:coordinates}");
        EntityManager.SpawnEntity(sm.CollisionResultPrototype, Transform(target).Coordinates);
        EntityManager.QueueDeleteEntity(target);

        args.Handled = true;
    }

    private void OnItemInteract(EntityUid uid, SupermatterComponent sm, ref InteractUsingEvent args)
    {
        var target = args.User;
        var item = args.Used;
        var othersFilter = Filter.Pvs(uid).RemovePlayerByAttachedEntity(target);

        if (args.Handled ||
            HasComp<GhostComponent>(target) ||
            HasComp<SupermatterImmuneComponent>(item) ||
            HasComp<GodmodeComponent>(item))
            return;

        if (HasComp<SupermatterImmuneComponent>(item) || HasComp<GodmodeComponent>(item))
            return;

        // TODO: supermatter scalpel
        if (HasComp<UnremoveableComponent>(item))
        {
            if (!sm.HasBeenPowered)
                LogFirstPower(uid, sm, target);

            var power = 200f;

            if (TryComp<PhysicsComponent>(target, out var targetPhysics))
                power += targetPhysics.Mass;

            if (TryComp<PhysicsComponent>(item, out var itemPhysics))
                power += itemPhysics.Mass;

            sm.MatterPower += power;

            _popup.PopupEntity(Loc.GetString("supermatter-collide-insert-unremoveable", ("target", target), ("sm", uid), ("item", item)), uid, othersFilter, true, PopupType.LargeCaution);
            _popup.PopupEntity(Loc.GetString("supermatter-collide-insert-unremoveable-user", ("sm", uid), ("item", item)), uid, target, PopupType.LargeCaution);
            _audio.PlayPvs(sm.DustSound, uid);

            // Prevent spam or excess power production
            AddComp<SupermatterImmuneComponent>(target);
            AddComp<SupermatterImmuneComponent>(item);

            _adminLog.Add(LogType.EntityDelete, LogImpact.High, $"{EntityManager.ToPrettyString(target):target} touched {EntityManager.ToPrettyString(uid):uid} with {EntityManager.ToPrettyString(item):item} and was destroyed at {Transform(uid).Coordinates:coordinates}");
            EntityManager.SpawnEntity(sm.CollisionResultPrototype, Transform(target).Coordinates);
            EntityManager.QueueDeleteEntity(target);
            EntityManager.QueueDeleteEntity(item);
        }
        else
        {
            if (!sm.HasBeenPowered)
                LogFirstPower(uid, sm, item);

            if (TryComp<PhysicsComponent>(item, out var physics))
                sm.MatterPower += physics.Mass;

            _popup.PopupEntity(Loc.GetString("supermatter-collide-insert", ("target", target), ("sm", uid), ("item", item)), uid, othersFilter, true, PopupType.LargeCaution);
            _popup.PopupEntity(Loc.GetString("supermatter-collide-insert-user", ("sm", uid), ("item", item)), uid, target, PopupType.LargeCaution);
            _audio.PlayPvs(sm.DustSound, uid);

            // Prevent spam or excess power production
            AddComp<SupermatterImmuneComponent>(item);

            _adminLog.Add(LogType.EntityDelete, LogImpact.High, $"{EntityManager.ToPrettyString(target):target} touched {EntityManager.ToPrettyString(uid):uid} with {EntityManager.ToPrettyString(item):item} and destroyed it at {Transform(uid).Coordinates:coordinates}");
            EntityManager.QueueDeleteEntity(item);
        }

        args.Handled = true;
    }

    private void OnGetSliver(EntityUid uid, SupermatterComponent sm, ref SupermatterDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // Your criminal actions will not go unnoticed
        sm.Damage += sm.DamageDelaminationPoint / 10;

        var integrity = GetIntegrity(sm).ToString("0.00");
        SendSupermatterAnnouncement(uid, sm, Loc.GetString("supermatter-announcement-cc-tamper", ("integrity", integrity)));

        Spawn(sm.SliverPrototype, Transform(args.User).Coordinates);
        _popup.PopupClient(Loc.GetString("supermatter-tamper-end"), uid, args.User);

        sm.DelamTimer /= 2;
    }

    private void OnGravPulse(Entity<SupermatterComponent> ent, ref GravPulseEvent args)
    {
        if (!TryComp<GravityWellComponent>(ent, out var gravityWell))
            return;

        var nextPulse = 0.5f * _random.NextFloat(1f, 30f);
        _gravityWell.SetPulsePeriod(ent, TimeSpan.FromSeconds(nextPulse), gravityWell);

        var audioParams = AudioParams.Default.WithMaxDistance(gravityWell.MaxRange);
        _audio.PlayPvs(ent.Comp.PullSound, ent, audioParams);
    }

    private void OnExamine(EntityUid uid, SupermatterComponent sm, ref ExaminedEvent args)
    {
        // For ghosts: alive players can use the console
        if (HasComp<GhostComponent>(args.Examiner) && args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("supermatter-examine-integrity", ("integrity", GetIntegrity(sm).ToString("0.00"))));
    }

    private void TryCollision(EntityUid uid, SupermatterComponent sm, EntityUid target, PhysicsComponent? targetPhysics = null, bool checkStatic = true)
    {
        if (!Resolve(target, ref targetPhysics))
            return;

        if (targetPhysics.BodyType == BodyType.Static && checkStatic ||
            HasComp<SupermatterImmuneComponent>(target) ||
            HasComp<GodmodeComponent>(target) ||
            _container.IsEntityInContainer(uid))
            return;

        if (!sm.HasBeenPowered)
            LogFirstPower(uid, sm, target);

        if (!HasComp<ProjectileComponent>(target))
        {
            var popup = "supermatter-collide";

            if (HasComp<MobStateComponent>(target))
            {
                popup = "supermatter-collide-mob";
                EntityManager.SpawnEntity(sm.CollisionResultPrototype, Transform(target).Coordinates);
                _chatManager.SendAdminAlert($"{EntityManager.ToPrettyString(uid):uid} has consumed {EntityManager.ToPrettyString(target):target}");
            }

            var targetProto = MetaData(target).EntityPrototype;
            if (targetProto != null && targetProto.ID != sm.CollisionResultPrototype)
            {
                _popup.PopupEntity(Loc.GetString(popup, ("sm", uid), ("target", target)), uid, PopupType.LargeCaution);
                _audio.PlayPvs(sm.DustSound, uid);
            }

            sm.MatterPower += targetPhysics.Mass;
            _adminLog.Add(LogType.EntityDelete, LogImpact.High, $"{EntityManager.ToPrettyString(target):target} collided with {EntityManager.ToPrettyString(uid):uid} at {Transform(uid).Coordinates:coordinates}");
        }

        // Prevent spam or excess power production
        AddComp<SupermatterImmuneComponent>(target);

        EntityManager.QueueDeleteEntity(target);

        if (TryComp<SupermatterFoodComponent>(target, out var food))
            sm.Power += food.Energy;
        else if (TryComp<ProjectileComponent>(target, out var projectile))
            sm.Power += (float)projectile.Damage.GetTotal();
        else
            sm.Power++;

        sm.MatterPower += HasComp<MobStateComponent>(target) ? 200 : 0;
    }

    private void LogFirstPower(EntityUid uid, SupermatterComponent sm, EntityUid target)
    {
        _adminLog.Add(LogType.Unknown, LogImpact.Extreme, $"{EntityManager.ToPrettyString(uid):uid} was powered for the first time by {EntityManager.ToPrettyString(target):target} at {Transform(uid).Coordinates:coordinates}");
        _chatManager.SendAdminAlert($"{EntityManager.ToPrettyString(uid):uid} was powered for the first time by {EntityManager.ToPrettyString(target):target}");
        sm.HasBeenPowered = true;
    }

    private void LogFirstPower(EntityUid uid, SupermatterComponent sm, GasMixture gas)
    {
        _adminLog.Add(LogType.Unknown, LogImpact.Extreme, $"{EntityManager.ToPrettyString(uid):uid} was powered for the first time by gas mixture at {Transform(uid).Coordinates:coordinates}");
        _chatManager.SendAdminAlert($"{EntityManager.ToPrettyString(uid):uid} was powered for the first time by gas mixture");
        sm.HasBeenPowered = true;
    }

    private SupermatterStatusType GetStatus(EntityUid uid, SupermatterComponent sm)
    {
        var mix = _atmosphere.GetContainingMixture(uid, true, true);

        if (mix is not { })
            return SupermatterStatusType.Error;

        if (sm.Delamming || sm.Damage >= sm.DamageDelaminationPoint)
            return SupermatterStatusType.Delaminating;

        if (sm.Damage >= sm.DamagePenaltyPoint)
            return SupermatterStatusType.Emergency;

        if (sm.Damage >= sm.DamageDelamAlertPoint)
            return SupermatterStatusType.Danger;

        if (sm.Damage >= sm.DamageWarningThreshold)
            return SupermatterStatusType.Warning;

        if (mix.Temperature > Atmospherics.T0C + _config.GetCVar(ECCVars.SupermatterHeatPenaltyThreshold) * 0.8)
            return SupermatterStatusType.Caution;

        if (sm.Power > 5)
            return SupermatterStatusType.Normal;

        return SupermatterStatusType.Inactive;
    }
}
