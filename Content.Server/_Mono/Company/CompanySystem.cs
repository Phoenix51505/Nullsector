using System.Diagnostics;
using Content.Shared._Mono.Company;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

/// <summary>
/// This system handles assigning a company to players when they join.
/// </summary>
public sealed class CompanySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly MetaDataSystem _metaSystem = default!;

    // Dictionary to store original company preferences for players
    private readonly Dictionary<string, string> _playerOriginalCompanies = new();

    private readonly HashSet<string> _ngcJobs =
    [
        "DirectorOfCare", // NOTE: NGC doesn't do much here. But humanitarian aid isn't bad.
    ];

    private readonly HashSet<string> _rogueJobs =
    [
        "PirateCaptain",
        "PirateFirstMate",
        "Pirate",
    ];

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to player spawn event to add the company component
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        // Subscribe to examination to show the company on examine
        SubscribeLocalEvent<CompanyComponent, ExaminedEvent>(OnExamined);

        // Subscribe to player detached event to clean up stored preferences
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        // Clean up stored preferences when player disconnects
        _playerOriginalCompanies.Remove(args.Player.UserId.ToString());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Add the company component with the player's saved company
        var companyComp = EnsureComp<CompanyComponent>(args.Mob);

        var playerId = args.Player.UserId.ToString();
        var profileCompany = args.Profile.Company;

        //Lua start: Login support
        foreach (var companyProto in _prototypeManager.EnumeratePrototypes<CompanyPrototype>())
        {
            if (!companyProto.Logins.Contains(args.Player.Name))
                continue; // Short-circuit
            companyComp.CompanyName = companyProto.ID;
            Dirty(args.Mob, companyComp);
            return;
        }
        //Lua end

        // Use "None" as fallback for empty company
        if (string.IsNullOrEmpty(profileCompany))
            profileCompany = "None";

        // Store the player's original company preference if not already stored
        if (!_playerOriginalCompanies.ContainsKey(playerId))
        {
            _playerOriginalCompanies[playerId] = profileCompany;
        }

        // Check if player's job is one of the NGC jobs
        if (args.JobId != null && _ngcJobs.Contains(args.JobId))
        {
            // Assign NGC company
            companyComp.CompanyName = "NGC";
        }
        // Check if player's job is one of the Rogue jobs
        else if (args.JobId != null && _rogueJobs.Contains(args.JobId))
        {
            // Assign Rogue company
            companyComp.CompanyName = "Rogue"; // WIP: Make this dynamic to each Armadan Subsidiary crew.
        }
        else
        {
            // Restore the player's original company preference
            companyComp.CompanyName = _playerOriginalCompanies[playerId];
        }

        // Start Null Sector
        if (companyComp.CompanyName.Equals("UnionJ"))
        {
            _metaSystem.SetEntityName(args.Mob, $"John {args.Profile.Name}");
            // Look man, Union J has its benefits, but it's got its downsides./
        }

        // Ensure the component is networked to clients
        Dirty(args.Mob, companyComp);
    }

    private void OnExamined(EntityUid uid, CompanyComponent component, ExaminedEvent args)
    {
        // Try to get the prototype for the company
        if (_prototypeManager.TryIndex<CompanyPrototype>(component.CompanyName, out var prototype) && component.CompanyName != "None")
        {
            // Use the color from the prototype with gender-appropriate pronoun
            args.PushMarkup(Loc.GetString("examine-company", 
                ("entity", uid), 
                ("company", $"[color={prototype.Color.ToHex()}]{prototype.Name}[/color]")), 
                priority: 100); // Much higher priority (100) will ensure it's at the top
        }
        else if (component.CompanyName != "None")
        {
            // Fallback for companies without prototypes
            args.PushMarkup(Loc.GetString("examine-company", 
                ("entity", uid), 
                ("company", $"[color=yellow]{component.CompanyName}[/color]")), 
                priority: 100);
        }
        // Don't show anything for "None" company
    }
}
