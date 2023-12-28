namespace CWTools.Games.CK3

open CWTools.Game
open CWTools.Localisation
open CWTools.Validation
open CWTools.Validation.ValidationCore
open CWTools.Games
open CWTools.Common
open System.IO
open CWTools.Validation.Common.CommonValidation
open CWTools.Rules
open CWTools.Process.Scopes
open System.Text
open CWTools.Games.LanguageFeatures
open CWTools.Validation.LocalisationString
open CWTools.Games.Helpers
open CWTools.Parser
open CWTools.Utilities.Utils
open FSharp.Collections.ParallelSeq
open CWTools.Process.Localisation

module CK3GameFunctions =
    type GameObject = GameObject<JominiComputedData, JominiLookup>

    let globalLocalisation (game: GameObject) =
        let validateProcessedLocalisation
            : ((Lang * LocKeySet) list -> (Lang * Map<string, LocEntry>) list -> ValidationResult) =
            validateProcessedLocalisationBase []

        let locParseErrors =
            game.LocalisationManager.LocalisationAPIs()
            <&!&> (fun (b, api) -> if b then validateLocalisationSyntax api.Results else OK)

        let globalTypeLoc = game.ValidationManager.ValidateGlobalLocalisation()

        game.Lookup.proccessedLoc
        |> validateProcessedLocalisation game.LocalisationManager.taggedLocalisationKeys
        <&&> locParseErrors
        <&&> globalTypeLoc
        |> (function
        | Invalid(_, es) -> es
        | _ -> [])

    let updateScriptedLoc (game: GameObject) = ()

    let updateModifiers (game: GameObject) =
        game.Lookup.coreModifiers <- game.Settings.embedded.modifiers

    let addModifiersWithScopes (lookup: Lookup) =
        let modifierCategoryToScopesMap () = Map.empty

        let modifierOptions (modifier: ActualModifier) =
            let requiredScopes = modifierCategoryManager.SupportedScopes modifier.category

            { Options.DefaultOptions with
                requiredScopes = requiredScopes }

        let processField =
            RulesParser.processTagAsField (scopeManager.ParseScope()) scopeManager.AnyScope scopeManager.ScopeGroups

        lookup.coreModifiers
        |> List.map (fun c ->
            AliasRule(
                "modifier",
                NewRule(LeafRule(processField c.tag, ValueField(ValueType.Float(-1E+12M, 1E+12M))), modifierOptions c)
            ))
    let afterInit (game: GameObject) =
        // updateScriptedTriggers()
        // updateScriptedEffects()
        // updateStaticodifiers()
        // updateScriptedLoc(game)
        // updateDefinedVariables()
        // updateProvinces(game)
        // updateCharacters(game)
        updateModifiers (game)

    // updateLegacyGovernments(game)
    // updateTechnologies()
    // game.LocalisationManager.UpdateAllLocalisation()
    // updateTypeDef game game.Settings.rules
    // game.LocalisationManager.UpdateAllLocalisation()


    let createEmbeddedSettings embeddedFiles cachedResourceData (configs: (string * string) list) cachedRuleMetadata =
        let scopeDefinitions =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "scopes.cwt")
            |> (fun f -> UtilityParser.initializeScopes f (Some []))

        configs
        |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifier_categories.cwt")
        |> (fun f -> UtilityParser.initializeModifierCategories f (Some []))

        let irMods =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "modifiers.cwt")
            |> Option.map (fun (fn, ft) -> UtilityParser.loadModifiers fn ft)
            |> Option.defaultValue []

        let irLocCommands =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "localisation.cwt")
            |> Option.map (fun (fn, ft) -> UtilityParser.loadLocCommands fn ft)
            |> Option.defaultValue ([], [], [])

        let jominiLocDataTypes =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "data_types.log")
            |> Option.map (fun (fn, ft) ->
                DataTypeParser.parseDataTypesStreamRes (
                    new MemoryStream(System.Text.Encoding.GetEncoding(1252).GetBytes(ft))
                ))
            |> Option.defaultValue
                { DataTypeParser.JominiLocDataTypes.promotes = Map.empty
                  confidentFunctions = Map.empty
                  DataTypeParser.JominiLocDataTypes.functions = Map.empty
                  DataTypeParser.JominiLocDataTypes.dataTypes = Map.empty
                  DataTypeParser.JominiLocDataTypes.dataTypeNames = Set.empty }

        let irEventTargetLinks =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "links.cwt")
            |> Option.map (fun (fn, ft) ->
                UtilityParser.loadEventTargetLinks
                    scopeManager.AnyScope
                    (scopeManager.ParseScope())
                    scopeManager.AllScopes
                    fn
                    ft)
            |> Option.defaultValue (CWTools.Process.Scopes.IR.scopedEffects |> List.map SimpleLink)

        let irEffects =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "effects.log")
            |> Option.bind (fun (fn, ft) ->
                JominiParser.parseEffectStreamRes (
                    new MemoryStream(System.Text.Encoding.GetEncoding(1252).GetBytes(ft))
                ))
            |> Option.map (JominiParser.processEffects scopeManager.ParseScopes)
            |> Option.defaultWith (fun () ->
                eprintfn "effects.log was not found in ck3 config"
                [])

        let irTriggers =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "triggers.log")
            |> Option.bind (fun (fn, ft) ->
                JominiParser.parseTriggerStreamRes (
                    new MemoryStream(System.Text.Encoding.GetEncoding(1252).GetBytes(ft))
                ))
            |> Option.map (JominiParser.processTriggers scopeManager.ParseScopes)
            |> Option.defaultWith (fun () ->
                eprintfn "triggers.log was not found in ck3 config"
                [])

        let featureSettings =
            configs
            |> List.tryFind (fun (fn, _) -> Path.GetFileName fn = "settings.cwt")
            |> Option.bind (fun (fn, ft) -> UtilityParser.loadSettingsFile fn ft)
            |> Option.defaultValue CWTools.Parser.UtilityParser.FeatureSettings.Default


        { triggers = irTriggers
          effects = irEffects
          modifiers = irMods
          embeddedFiles = embeddedFiles
          cachedResourceData = cachedResourceData
          localisationCommands = Jomini jominiLocDataTypes
          eventTargetLinks = irEventTargetLinks
          cachedRuleMetadata = cachedRuleMetadata
          featureSettings = featureSettings }

type CK3Settings = GameSetupSettings<JominiLookup>

open CK3GameFunctions
open CWTools.Localisation.CK3

type CK3Game(setupSettings: CK3Settings) =
    let validationSettings =
        { validators = [ validateIfWithNoEffect, "ifnoeffect" ]
          experimentalValidators = []
          heavyExperimentalValidators = []
          experimental = false
          fileValidators = []
          lookupValidators = commonValidationRules
          lookupFileValidators = []
          useRules = true
          debugRulesOnly = false
          localisationValidators = [] }

    let embeddedSettings =
        match setupSettings.embedded with
        | FromConfig(ef, crd) ->
            createEmbeddedSettings
                ef
                crd
                (setupSettings.rules
                 |> Option.map (fun r -> r.ruleFiles)
                 |> Option.defaultValue [])
                None
        | Metadata cmd ->
            createEmbeddedSettings
                []
                []
                (setupSettings.rules
                 |> Option.map (fun r -> r.ruleFiles)
                 |> Option.defaultValue [])
                (Some cmd)
        | ManualSettings e -> e

    let settings =
        { rootDirectories = setupSettings.rootDirectories
          excludeGlobPatterns = setupSettings.excludeGlobPatterns
          embedded = embeddedSettings
          GameSettings.rules = setupSettings.rules
          validation = setupSettings.validation
          scriptFolders = setupSettings.scriptFolders
          modFilter = setupSettings.modFilter
          initialLookup = JominiLookup()
          maxFileSize = setupSettings.maxFileSize
          enableInlineScripts = false

        }

    do
        if scopeManager.Initialized |> not then
            eprintfn "%A has no scopes" (settings.rootDirectories |> List.head)
        else
            ()

    let locCommands () = []

    let settings =
        { settings with
            initialLookup = JominiLookup() }

    let changeScope =
        Scopes.createJominiChangeScope
            CWTools.Process.Scopes.CK3.oneToOneScopes
            (Scopes.complexVarPrefixFun "variable:from:" "variable:")

    let jominiLocDataTypes =
        settings.embedded.localisationCommands
        |> function
            | Jomini dts -> Some dts
            | _ -> None

    let processLocalisationFunction lookup =
        (createJominiLocalisationFunctions jominiLocDataTypes lookup)

    // let validationLocalisationCommandFunction lookup =
    // createJominiLocalisationFunctions jominiLocDataTypes lookup |> snd

    let rulesManagerSettings =
        { rulesSettings = settings.rules
          useFormulas = true
          stellarisScopeTriggers = false
          parseScope = scopeManager.ParseScope()
          allScopes = scopeManager.AllScopes
          anyScope = scopeManager.AnyScope
          scopeGroups = scopeManager.ScopeGroups
          changeScope = changeScope
          defaultContext = CWTools.Process.Scopes.Scopes.defaultContext
          defaultLang = CK3 CK3Lang.English
          oneToOneScopesNames = CWTools.Process.Scopes.CK3.oneToOneScopesNames
          loadConfigRulesHook = Hooks.loadConfigRulesHook addModifiersWithScopes
          refreshConfigBeforeFirstTypesHook = Hooks.refreshConfigBeforeFirstTypesHook
          refreshConfigAfterFirstTypesHook = Hooks.refreshConfigAfterFirstTypesHook true
          refreshConfigAfterVarDefHook = Hooks.refreshConfigAfterVarDefHook true
          locFunctions = processLocalisationFunction }

    let scriptFolders = []

    let game =
        GameObject<JominiComputedData, JominiLookup>.CreateGame
            ((settings,
              "crusader kings iii",
              scriptFolders,
              Compute.Jomini.computeJominiData,
              Compute.Jomini.computeJominiDataUpdate,
              (CK3LocalisationService >> (fun f -> f :> ILocalisationAPICreator)),
              processLocalisationFunction,
              CWTools.Process.Scopes.Scopes.defaultContext,
              CWTools.Process.Scopes.Scopes.noneContext,
              Encoding.UTF8,
              Encoding.GetEncoding(1252),
              validationSettings,
              globalLocalisation,
              (fun _ _ -> ()),
              ".yml",
              rulesManagerSettings))
            afterInit

    let lookup = game.Lookup
    let resources = game.Resources
    let fileManager = game.FileManager

    let references =
        References<_>(resources, lookup, (game.LocalisationManager.LocalisationAPIs() |> List.map snd))


    let parseErrors () =
        resources.GetResources()
        |> List.choose (function
            | EntityResource(_, e) -> Some e
            | _ -> None)
        |> List.choose (fun r ->
            r.result
            |> function
                | Fail result when r.validate -> Some(r.filepath, result.error, result.position)
                | _ -> None)

    interface IGame<JominiComputedData> with
        member __.ParserErrors() = parseErrors ()

        member __.ValidationErrors() =
            let s, d = game.ValidationManager.Validate(false, resources.ValidatableEntities()) in s @ d

        member __.LocalisationErrors(force: bool, forceGlobal: bool) =
            getLocalisationErrors game globalLocalisation (force, forceGlobal)

        member __.Folders() = fileManager.AllFolders()
        member __.AllFiles() = resources.GetResources()

        member __.AllLoadedLocalisation() =
            game.LocalisationManager.LocalisationFileNames()

        member __.ScriptedTriggers() = lookup.triggers
        member __.ScriptedEffects() = lookup.effects
        member __.StaticModifiers() = [] //lookup.staticModifiers
        member __.UpdateFile shallow file text = game.UpdateFile shallow file text
        member __.AllEntities() = resources.AllEntities()

        member __.References() =
            References<_>(resources, lookup, (game.LocalisationManager.LocalisationAPIs() |> List.map snd))

        member __.Complete pos file text =
            completion fileManager game.completionService game.InfoService game.ResourceManager pos file text

        member __.ScopesAtPos pos file text =
            scopesAtPos fileManager game.ResourceManager game.InfoService scopeManager.AnyScope pos file text

        member __.GoToType pos file text =
            getInfoAtPos
                fileManager
                game.ResourceManager
                game.InfoService
                game.LocalisationManager
                lookup
                (CK3 CK3Lang.English)
                pos
                file
                text

        member __.FindAllRefs pos file text =
            findAllRefsFromPos fileManager game.ResourceManager game.InfoService pos file text

        member __.InfoAtPos pos file text = game.InfoAtPos pos file text

        member __.ReplaceConfigRules rules =
            game.ReplaceConfigRules
                { ruleFiles = rules
                  validateRules = true
                  debugRulesOnly = false
                  debugMode = false } //refreshRuleCaches game (Some { ruleFiles = rules; validateRules = true; debugRulesOnly = false; debugMode = false})

        member __.RefreshCaches() = game.RefreshCaches()

        member __.RefreshLocalisationCaches() =
            game.LocalisationManager.UpdateProcessedLocalisation()

        member __.ForceRecompute() = resources.ForceRecompute()
        member __.Types() = game.Lookup.typeDefInfo
        member __.TypeDefs() = game.Lookup.typeDefs
        member __.GetPossibleCodeEdits file text = []
        member __.GetCodeEdits file text = None

        member __.GetEventGraphData: GraphDataRequest =
            (fun files gameType depth ->
                graphEventDataForFiles references game.ResourceManager lookup files gameType depth)

        member __.GetEmbeddedMetadata() =
            getEmbeddedMetadata lookup game.LocalisationManager game.ResourceManager
