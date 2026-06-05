using ItemModel = Amium.Items.Item;
using Amium.Items;
using Amium.Item.Client;
using Avalonia.Media;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Functions;
using HornetStudio.Editor.Monitoring;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.Persistence;
using HornetStudio.Editor.UdlClients;
using HornetStudio.Editor.ViewModels;
using HornetStudio.Editor.Widgets;
using HornetStudio.Editor.Widgets.Workflow;
using HornetStudio.Host;
using HornetStudio.Host.Python.Client;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

var tests = new (string Name, Action Run)[]
{
    ("Demo sample keeps UDL definition file backed", DemoSampleKeepsUdlDefinitionFileBacked),
    ("UDL client file codec derives id from file name", UdlClientFileCodecDerivesIdFromFileName),
    ("Clients browser enumerates UDL files from folder local Clients/Udl", ClientsBrowserEnumeratesUdlFilesFromFolderLocalClientsDirectory),
    ("Clients browser entry maps UDL list fields", ClientsBrowserEntryMapsUdlListFields),
    ("UDL client file codec roundtrips and renames", UdlClientFileCodecRoundtripsAndRenames),
    ("UDL client file codec roundtrips module exposures", UdlClientFileCodecRoundtripsModuleExposures),
    ("UDL client file codec roundtrips route-only module exposure", UdlClientFileCodecRoundtripsRouteOnlyModuleExposure),
    ("UDL module exposure dialog adds fallback bitmask rows for module scope", UdlModuleExposureDialogAddsFallbackBitmaskRowsForModuleScope),
    ("UDL module exposure dialog saves fallback publish bits without manual count edit", UdlModuleExposureDialogSavesFallbackPublishBitsWithoutManualCountEdit),
    ("UDL module exposure dialog saves route-only read helper setting", UdlModuleExposureDialogSavesRouteOnlyReadHelperSetting),
    ("UDL module exposure dialog reopens saved route-only read helper setting", UdlModuleExposureDialogReopensSavedRouteOnlyReadHelperSetting),
    ("UDL module exposure dialog preserves saved fallback bit settings", UdlModuleExposureDialogPreservesSavedFallbackBitSettings),
    ("UDL module exposure dialog keeps runtime bit format precedence", UdlModuleExposureDialogKeepsRuntimeBitFormatPrecedence),
    ("UDL file save and sync updates folder runtime definitions", UdlFileSaveAndSyncUpdatesFolderRuntimeDefinitions),
    ("UDL runtime manager connects simulated client", UdlRuntimeManagerConnectsSimulatedClient),
    ("UDL runtime manager projects status without widget", UdlRuntimeManagerProjectsStatusWithoutWidget),
    ("UDL runtime manager publishes attached items without widget", UdlRuntimeManagerPublishesAttachedItemsWithoutWidget),
    ("UDL runtime manager pendingconnect duplicate sync does not recreate attached roots", UdlRuntimeManagerDuplicateAutoConnectSyncDoesNotRecreateAttachedRoots),
    ("UDL runtime manager refresh does not recreate attached roots", UdlRuntimeManagerRefreshDoesNotRecreateAttachedRoots),
    ("UDL runtime manager removes legacy exposure root", UdlRuntimeManagerRemovesLegacyExposureRoot),
    ("UDL runtime manager publishes exposed bits without widget", UdlRuntimeManagerPublishesExposedBitsWithoutWidget),
    ("UDL runtime manager writes exposed set bits without widget", UdlRuntimeManagerWritesExposedSetBitsWithoutWidget),
    ("UDL runtime manager updates exposed set bits from numeric mask without widget", UdlRuntimeManagerUpdatesExposedSetBitsFromNumericMaskWithoutWidget),
    ("UDL runtime manager routes read bit writes to set without widget", UdlRuntimeManagerRoutesReadBitWritesToSetWithoutWidget),
    ("UDL runtime manager routed read bits follow set mask without widget", UdlRuntimeManagerRoutedReadBitsFollowSetMaskWithoutWidget),
    ("UDL runtime manager publishes bit exposure parity across channels without widget", UdlRuntimeManagerPublishesBitExposureParityAcrossChannelsWithoutWidget),
    ("UDL exposure projection detects active bit exposures", UdlExposureProjectionDetectsActiveBitExposures),
    ("UDL simulated demo scheduler keeps periodic cadence under handler load", UdlSimulatedDemoSchedulerKeepsPeriodicCadenceUnderHandlerLoad),
    ("UDL simulated demo low value reads skip unchanged assignments", UdlSimulatedDemoLowValueReadsSkipUnchangedAssignments),
    ("UDL runtime manager removes attached projection after detach update", UdlRuntimeManagerRemovesAttachedProjectionAfterDetachUpdate),
    ("UDL runtime manager keeps raw items private without attach", UdlRuntimeManagerKeepsRawItemsPrivateWithoutAttach),
    ("UDL runtime manager release clears projection state", UdlRuntimeManagerReleaseClearsProjectionState),
    ("UDL runtime manager release folder clears connected runtimes", UdlRuntimeManagerReleaseFolderClearsConnectedRuntimes),
    ("Custom signal codec parses YAML style nodes", CustomSignalCodecParsesYamlStyleNodes),
    ("Path identity validation accepts only snake_case", PathIdentityValidationAcceptsOnlySnakeCase),
    ("Folder identity validation accepts only snake_case", FolderIdentityValidationAcceptsOnlySnakeCase),
    ("Folder identity defaults use snake_case", FolderIdentityDefaultsUseSnakeCase),
    ("Custom signal editor defaults to snake_case name", CustomSignalEditorDefaultsToSnakeCaseName),
    ("Custom signal editor rejects uppercase name", CustomSignalEditorRejectsUppercaseName),
    ("Custom signal manual trigger path uses lowercase suffix", CustomSignalManualTriggerPathUsesLowercaseSuffix),
    ("Custom signal publish snapshot adds type metadata", CustomSignalPublishSnapshotAddsTypeMetadata),
    ("Custom signal manual trigger publishes bool type metadata", CustomSignalManualTriggerPublishesBoolTypeMetadata),
    ("Custom signal source change filter accepts matching source path", CustomSignalSourceChangeFilterAcceptsMatchingSourcePath),
    ("Custom signal source change filter rejects unrelated path", CustomSignalSourceChangeFilterRejectsUnrelatedPath),
    ("Custom signal enumerate source paths yields all configured sources", CustomSignalEnumerateSourcePathsYieldsAllConfiguredSources),
    ("Scoped registry filter matches descendant path without sibling bleed", ScopedRegistryFilterMatchesDescendantPathWithoutSiblingBleed),
    ("Scoped registry filter can reject ancestor paths", ScopedRegistryFilterCanRejectAncestorPaths),
    ("Scoped registry filter collapses redundant prefixes", ScopedRegistryFilterCollapsesRedundantPrefixes),
    ("Monitor codec preserves multiple actions per trigger", MonitorCodecPreservesMultipleActionsPerTrigger),
    ("Monitor editor accepts 100 ms refresh rate", MonitorEditorAccepts100MsRefreshRate),
    ("Monitor codec preserves action specific fields", MonitorCodecPreservesActionSpecificFields),
    ("Monitor editor accepts multiple actions per trigger", MonitorEditorAcceptsMultipleActionsPerTrigger),
    ("Monitor editor rejects WriteLog actions without target", MonitorEditorRejectsWriteLogActionWithoutTarget),
    ("Monitor file codec roundtrips central Monitor.yaml", MonitorFileCodecRoundtripsCentralYaml),
    ("Monitor file codec loads custom rule without source", MonitorFileCodecLoadsCustomRuleWithoutSource),
    ("Monitor registry prefers central file definitions", MonitorRegistryPrefersCentralFileDefinitions),
    ("Monitor runtime manager publishes without widget", MonitorRuntimeManagerPublishesWithoutWidget),
    ("Monitor runtime manager updates changed definition with same name", MonitorRuntimeManagerUpdatesChangedDefinitionWithSameName),
    ("Main window monitor sync keeps background runtime with monitor browser", MainWindowMonitorSyncKeepsBackgroundRuntimeWithMonitorBrowser),
    ("Main window monitor sync loads central definitions", MainWindowMonitorSyncLoadsCentralDefinitions),
    ("Signal history runtime skips zero history", SignalHistoryRuntimeSkipsZeroHistory),
    ("Signal history runtime records configured source", SignalHistoryRuntimeRecordsConfiguredSource),
    ("Chart data provider returns empty series without history", ChartDataProviderReturnsEmptySeriesWithoutHistory),
    ("Monitor browser resolves central definitions", MonitorBrowserResolvesCentralDefinitions),
    ("Monitor browser resolves central definitions without view model", MonitorBrowserResolvesCentralDefinitionsWithoutViewModel),
    ("Monitor control detach keeps runtime aggregate", MonitorControlDetachKeepsRuntimeAggregate),
    ("Monitor view selection normalizes ids", MonitorViewSelectionNormalizesIds),
    ("Monitor view selection rejects duplicate ids", MonitorViewSelectionRejectsDuplicateIds),
    ("Monitor picker uses stable rule labels", MonitorPickerUsesStableRuleLabels),
    ("Monitor view resolves only selected rules", MonitorViewResolvesOnlySelectedRules),
    ("Monitor view rebuilds selected rules after project runtime ready", MonitorViewRebuildsSelectedRulesAfterProjectRuntimeReady),
    ("Monitor view runtime generation advances for repeated project load", MonitorViewRuntimeGenerationAdvancesForRepeatedProjectLoad),
    ("Monitor YAML control definition writes monitor definitions", MonitorYamlControlDefinitionWritesMonitorDefinitions),
    ("Project UI YAML loader imports monitor definitions", ProjectUiYamlLoaderImportsMonitorDefinitions),
    ("Workflow codec parses YAML steps", WorkflowCodecParsesYamlSteps),
    ("Workflow codec rejects missing name", WorkflowCodecRejectsMissingName),
    ("Workflow codec parses IfThenElse variables", WorkflowCodecParsesIfThenElseVariables),
    ("Workflow codec parses While", WorkflowCodecParsesWhile),
    ("Workflow codec rejects While without positive delay guard", WorkflowCodecRejectsWhileWithoutPositiveDelayGuard),
    ("Workflow codec parses SetValue valueFrom", WorkflowCodecParsesSetValueValueFrom),
    ("Workflow codec serializes SetValue valueFrom", WorkflowCodecSerializesSetValueValueFrom),
    ("Workflow codec serializes structured SetValue value", WorkflowCodecSerializesStructuredSetValueValue),
    ("Workflow editor row roundtrips SetValue valueFrom", WorkflowEditorRowRoundtripsSetValueValueFrom),
    ("Workflow editor row maps legacy valueFrom to structured SetFromItem", WorkflowEditorRowMapsLegacyValueFromToStructuredSetFromItem),
    ("Workflow editor creates While default delay guard", WorkflowEditorCreatesWhileDefaultDelayGuard),
    ("Workflow editor converter edits While steps", WorkflowEditorConverterEditsWhileSteps),
    ("Workflow executor SetValue resolves valueFrom", WorkflowExecutorSetValueResolvesValueFrom),
    ("Workflow executor SetValue fails unresolved valueFrom", WorkflowExecutorSetValueFailsUnresolvedValueFrom),
    ("Workflow executor SetValue executes structured increment", WorkflowExecutorSetValueExecutesStructuredIncrement),
    ("Interaction rule codec roundtrip preserves RunFunction", InteractionRuleCodecRoundtripPreservesRunFunction),
    ("Interaction rule codec roundtrip preserves StopFunction", InteractionRuleCodecRoundtripPreservesStopFunction),
    ("SetValue operation codec roundtrip preserves structured payload", SetValueOperationCodecRoundtripPreservesStructuredPayload),
    ("SetValue operation codec keeps legacy literal fallback", SetValueOperationCodecKeepsLegacyLiteralFallback),
    ("SetValue inline options match target kind", SetValueInlineOptionsMatchTargetKind),
    ("SetValue inline editor maps parseable boolean literals", SetValueInlineEditorMapsParseableBooleanLiterals),
    ("SetValue inline editor row serializes numeric delta", SetValueInlineEditorRowSerializesNumericDelta),
    ("SetValue inline editor row serializes boolean true", SetValueInlineEditorRowSerializesBooleanTrue),
    ("SetValue inline editor row preserves invalid legacy boolean literal", SetValueInlineEditorRowPreservesInvalidLegacyBooleanLiteral),
    ("SetValue inline editor row serializes string append separator", SetValueInlineEditorRowSerializesStringAppendSeparator),
    ("SetValue inline editor row reports invalid numeric literal", SetValueInlineEditorRowReportsInvalidNumericLiteral),
    ("SetValue inline editor row loads legacy literal as equals", SetValueInlineEditorRowLoadsLegacyLiteralAsEquals),
    ("SetValue inline editor row uses item source path", SetValueInlineEditorRowUsesItemSourcePath),
    ("SetValue operation summary formats structured operations", SetValueOperationSummaryFormatsStructuredOperations),
    ("SetValue runtime append inserts separator only when needed", SetValueRuntimeAppendInsertsSeparatorOnlyWhenNeeded),
    ("SetValue operation validation rejects unsupported numeric operation", SetValueOperationValidationRejectsUnsupportedNumericOperation),
    ("Target value type parser normalizes canonical values", TargetValueTypeParserNormalizesCanonicalValues),
    ("SetValue target classification prefers explicit type over empty value", SetValueTargetClassificationPrefersExplicitTypeOverEmptyValue),
    ("SetValue descriptor resolves numeric kind for row target path", SetValueDescriptorResolvesNumericKindForRowTargetPath),
    ("SetValue descriptor preserves unresolved requested row target path", SetValueDescriptorPreservesUnresolvedRequestedRowTargetPath),
    ("SetValue source options allow readonly compatible float source", SetValueSourceOptionsAllowReadonlyCompatibleFloatSource),
    ("SetValue source validation resolves compatible float source directly", SetValueSourceValidationResolvesCompatibleFloatSourceDirectly),
    ("Function registry creates declarative entries", FunctionRegistryCreatesDeclarativeEntries),
    ("Function registry combines declarative and Python entries", FunctionRegistryCombinesDeclarativeAndPythonEntries),
    ("Function registry surfaces invalid declarative files", FunctionRegistrySurfacesInvalidDeclarativeFiles),
    ("Function registry resolves stable references", FunctionRegistryResolvesStableReferences),
    ("Function registry resolves Python stable references", FunctionRegistryResolvesPythonStableReferences),
    ("RunFunction options include runnable Python entries", RunFunctionOptionsIncludeRunnablePythonEntries),
    ("RunFunction editor keeps argument visible", RunFunctionEditorKeepsArgumentVisible),
    ("Function registry resolves yaml alias", FunctionRegistryResolvesYamlAlias),
    ("RunFunction picker uses display labels", RunFunctionPickerUsesDisplayLabels),
    ("RunFunction picker keeps missing reference visible", RunFunctionPickerKeepsMissingReferenceVisible),
    ("Workflow editor converter creates editable rows", WorkflowEditorConverterCreatesEditableRows),
    ("Boolean condition editor adds variables", BooleanConditionEditorAddsVariables),
    ("Workflow editor converter edits IfThenElse steps", WorkflowEditorConverterEditsIfThenElseSteps),
    ("Workflow row condition editing commits cloned state", WorkflowRowConditionEditingCommitsClonedState),
    ("Workflow executor runs steps in order", WorkflowExecutorRunsStepsInOrder),
    ("Workflow executor resolves step local condition variables", WorkflowExecutorResolvesStepLocalConditionVariables),
    ("Workflow executor runs While until condition becomes false", WorkflowExecutorRunsWhileUntilConditionBecomesFalse),
    ("Workflow executor controlled stop completes as done", WorkflowExecutorControlledStopCompletesAsDone),
    ("StopFunction editor uses RunFunction picker behavior", StopFunctionEditorUsesRunFunctionPickerBehavior),
    ("Workflow executor fails missing condition variable source", WorkflowExecutorFailsMissingConditionVariableSource),
    ("Workflow executor reports cancellation state transitions", WorkflowExecutorReportsCancellationStateTransitions),
    ("Workflow executor fails missing explicit log target", WorkflowExecutorFailsMissingExplicitLogTarget),
    ("Runtime YAML loader maps workflow widget controls", RuntimeYamlLoaderMapsWorkflowWidgetControls),
    ("CreateItem applies workflow widget defaults", CreateItemAppliesWorkflowWidgetDefaults),
    ("Runtime YAML loader maps monitor view controls", RuntimeYamlLoaderMapsMonitorViewControls),
    ("Browser diagnostics source includes folder scope", BrowserDiagnosticsSourceIncludesFolderScope),
    ("Browser diagnostics source falls back to item name", BrowserDiagnosticsSourceFallsBackToItemName),
    ("UI diagnostics backlog counters stay non-negative", UiDiagnosticsBacklogCountersStayNonNegative),
    ("UI diagnostics benchmark snapshot captures warmup backlog", UiDiagnosticsBenchmarkSnapshotCapturesWarmupBacklog),
    ("Monitor view row ignores unchanged aggregate updates", MonitorViewRowIgnoresUnchangedAggregateUpdates),
    ("Monitor view row ignores aggregate active changes", MonitorViewRowIgnoresAggregateActiveChanges),
    ("Monitor view row accepts rule root changes", MonitorViewRowAcceptsRuleRootChanges),
    ("Monitor view row accepts active changes", MonitorViewRowAcceptsActiveChanges),
    ("Monitor view row updates after delayed runtime publish", MonitorViewRowUpdatesAfterDelayedRuntimePublish),
    ("Monitor view row ignores unselected rule changes", MonitorViewRowIgnoresUnselectedRuleChanges),
    ("Monitor view row accepts message changes", MonitorViewRowAcceptsMessageChanges),
    ("Monitor view row ignores redundant message updates", MonitorViewRowIgnoresRedundantMessageUpdates),
    ("Monitor view row uses configured active color", MonitorViewRowUsesConfiguredActiveColor),
    ("Canvas widget picker excludes Functions", CanvasWidgetPickerExcludesFunctions),
    ("Admin browser visibility follows current user level", AdminBrowserVisibilityFollowsCurrentUserLevel),
    ("VisualRule codec roundtrip", VisualRuleCodecRoundtrip),
    ("VisualRule source path display hides technical monitor prefix", VisualRuleSourcePathDisplayHidesTechnicalMonitorPrefix),
    ("VisualRule layout document roundtrip", VisualRuleLayoutDocumentRoundtrip),
    ("Project UI YAML loader keeps screen definitions scalar compatible", ProjectUiYamlLoaderKeepsScreenDefinitionsScalarCompatible),
    ("Runtime YAML loader maps dialog widget controls", RuntimeYamlLoaderMapsDialogWidgetControls),
    ("Dialog interaction rules persist dialog widget ids", DialogInteractionRulesPersistDialogWidgetIds),
    ("StopFunction interaction rules persist function name", StopFunctionInteractionRulesPersistFunctionName),
    ("Dialog widget picker only lists dialog widgets", DialogWidgetPickerOnlyListsDialogWidgets),
    ("CreateItem applies requested caption visibility defaults", CreateItemAppliesRequestedCaptionVisibilityDefaults),
    ("OpenDialog keeps one overlay per dialog widget", OpenDialogKeepsOneOverlayPerDialogWidget),
    ("OpenDialog applies default placement", OpenDialogAppliesDefaultPlacement),
    ("OpenDialog preserves dialog grid child placement", OpenDialogPreservesDialogGridChildPlacement),
    ("OpenDialog uses dialog widget bounds", OpenDialogUsesDialogWidgetBounds),
    ("Project UI YAML loader imports visual rules", ProjectUiYamlLoaderImportsVisualRules),
    ("Signal visual rule applies body back color", SignalVisualRuleAppliesBodyBackColor),
    ("Button visual rule applies button back color", ButtonVisualRuleAppliesButtonBackColor),
    ("Circle display visual rule applies display back color", CircleDisplayVisualRuleAppliesDisplayBackColor),
    ("Monitor SetValue transition action writes target value", MonitorSetValueTransitionActionWritesTargetValue),
    ("Monitor SetValue clear action remains stable for independent target", MonitorSetValueClearActionRemainsStableForIndependentTarget),
    ("Monitor WriteLog resolves relative owned log path", MonitorWriteLogResolvesRelativeOwnedLogPath),
    ("Monitor aggregate metadata includes active event texts", MonitorAggregateMetadataIncludesActiveEventTexts),
    ("Monitor control ignores non-monitor items", MonitorControlIgnoresNonMonitorItems),
    ("Monitor row visuals highlight active severity", MonitorRowVisualsHighlightActiveSeverity),
    ("Monitor editor auto assigns next free EventId", MonitorEditorAutoAssignsNextFreeEventId),
    ("Monitor editor rejects duplicate EventId", MonitorEditorRejectsDuplicateEventId),
    ("Monitor editor rejects duplicate EventId from central file with duplicate names", MonitorEditorRejectsDuplicateEventIdFromCentralFileWithDuplicateNames),
    ("Monitor editor rejects blank and zero EventId", MonitorEditorRejectsBlankAndZeroEventId),
    ("Monitor editor allows unchanged EventId when editing", MonitorEditorAllowsUnchangedEventIdWhenEditing),
    ("Enhanced signal editor defaults to snake_case name", EnhancedSignalEditorDefaultsToSnakeCaseName),
    ("Enhanced signal editor rejects uppercase name", EnhancedSignalEditorRejectsUppercaseName),
    ("Enhanced signal runtime path uses snake_case segments", EnhancedSignalRuntimePathUsesSnakeCaseSegments),
    ("Python application runtime path uses snake_case segments", PythonApplicationRuntimePathUsesSnakeCaseSegments),
    ("Application explorer registry root uses snake_case segments", ApplicationExplorerRegistryRootUsesSnakeCaseSegments),
    ("Circle display runtime path uses snake_case segments", CircleDisplayRuntimePathUsesSnakeCaseSegments),
    ("Csv logger runtime path uses snake_case segments", CsvLoggerRuntimePathUsesSnakeCaseSegments),
    ("Sql logger runtime path uses snake_case segments", SqlLoggerRuntimePathUsesSnakeCaseSegments),
    ("LogControl owned path uses folder identity", LogControlOwnedPathUsesFolderIdentity),
    ("LogControl owned directory uses project logs folder", LogControlOwnedDirectoryUsesProjectLogsFolder),
    ("LogControl legacy values do not control owned path", LogControlLegacyValuesDoNotControlOwnedPath),
    ("LogControl YAML omits legacy log properties", LogControlYamlOmitsLegacyLogProperties),
    ("LogControl document serialization omits legacy log properties", LogControlDocumentSerializationOmitsLegacyLogProperties),
    ("LogControl ensures owned process log publication", LogControlEnsuresOwnedProcessLogPublication),
    ("Logger runtime control constants use snake_case", LoggerRuntimeControlConstantsUseSnakeCase),
    ("Item client mode defaults to external", ItemClientModeDefaultsToExternal),
    ("Item client mode normalizes values", ItemClientModeNormalizesValues),
    ("Item client base topic allows empty", ItemClientBaseTopicAllowsEmpty),
    ("Item client layout document defaults to external mode", ItemClientLayoutDocumentDefaultsToExternalMode),
    ("Item client publish items default empty", ItemClientPublishItemsDefaultEmpty),
    ("Item client layout publish items default empty", ItemClientLayoutPublishItemsDefaultEmpty),
    ("Item client publish options use metadata", ItemClientPublishOptionsUseMetadata),
    ("Item client publish options exclude broker received items", ItemClientPublishOptionsExcludeBrokerReceivedItems),
    ("Item client publish options de-duplicate legacy roots", ItemClientPublishOptionsDeduplicateLegacyRoots),
    ("ItemModel tree visibility uses display metadata", ItemTreeVisibilityUsesDisplayMetadata),
    ("Broker attach options use internal discovery", BrokerAttachOptionsUseInternalDiscovery),
    ("Item client publish items renders flat attach rows", ItemClientPublishItemsRendersFlatAttachRows),
    ("Item client attached body row hides widget prefix", ItemClientAttachedBodyRowHidesWidgetPrefix),
    ("Item client published body row hides Studio folder prefix", ItemClientPublishedBodyRowHidesStudioFolderPrefix),
    ("Item client published dialog shows root row and hides folder prefix", ItemClientPublishedDialogShowsRootRowAndHidesFolderPrefix),
    ("Item client received path uses flat widget branch", ItemClientReceivedPathUsesFlatWidgetBranch),
    ("Item client received path collapses nested MQTT identity", ItemClientReceivedPathCollapsesNestedMqttIdentity),
    ("Item client attach identity collapses nested MQTT identity", ItemClientAttachIdentityCollapsesNestedMqttIdentity),
    ("Item client attach identity includes base topic", ItemClientAttachIdentityIncludesBaseTopic),
    ("Item client attach options use item values", ItemClientAttachOptionsUseItemValues),
    ("Item client attach option path splits dotted identity", ItemClientAttachOptionPathSplitsDottedIdentity),
    ("Broker attach normalization strips prefix before MQTT identity", BrokerAttachNormalizationStripsPrefixBeforeMqttIdentity),
    ("Item client attach selection normalizes legacy shared path", ItemClientAttachSelectionNormalizesLegacySharedPath),
    ("UDL attach add normalizes and de-duplicates paths", UdlAttachAddNormalizesAndDeduplicatesPaths),
    ("UDL attach remove clears selected path", UdlAttachRemoveClearsSelectedPath),
    ("UDL received rows stay visible when attached", UdlReceivedRowsStayVisibleWhenAttached),
    ("UDL attached items resolve via runtime registry", UdlAttachedItemsResolveViaRuntimeRegistry),
    ("UDL runtime received items stay out of registry until attach", UdlRuntimeReceivedItemsStayOutOfRegistryUntilAttach),
    ("UDL read attachment publishes value reference metadata", UdlReadAttachmentPublishesValueReferenceMetadata),
    ("UDL read attachment keeps value reference metadata after snapshot refresh", UdlReadAttachmentKeepsValueReferenceMetadataAfterSnapshotRefresh),
    ("UDL value reference warmup resync adds only new public roots", UdlValueReferenceWarmupResyncAddsOnlyNewPublicRoots),
    ("UDL projection clears detached public items", UdlProjectionClearsDetachedPublicItems),
    ("UDL runtime isolates attached paths per client", UdlRuntimeIsolatesAttachedPathsPerClient),
    ("UDL set-driven demo writes feedback to read only", UdlSetDrivenDemoWritesFeedbackToReadOnly),
    ("UDL simulated demo publishes channel type metadata", UdlSimulatedDemoPublishesChannelTypeMetadata),
    ("UDL runtime channels include registry items", UdlRuntimeChannelsIncludeRegistryItems),
    ("UDL module publishes channel type metadata", UdlModulePublishesChannelTypeMetadata),
    ("UDL runtime exposure bits use snake_case paths", UdlRuntimeExposureBitsUseSnakeCasePaths),
    ("Target path normalization uses Studio root", TargetPathNormalizationUsesStudioRoot),
    ("Broker published item codec migrates legacy paths", BrokerPublishedItemCodecMigratesLegacyPaths),
    ("Broker published item codec keeps explicit Studio broker paths", BrokerPublishedItemCodecKeepsExplicitStudioBrokerPaths),
    ("Broker published item codec keeps explicit HornetStudio broker paths", BrokerPublishedItemCodecKeepsExplicitHornetStudioBrokerPaths),
    ("Broker published item codec roundtrip", BrokerPublishedItemCodecRoundtrip),
    ("Broker published item codec filters active root definitions", BrokerPublishedItemCodecFiltersActiveRootDefinitions),
    ("Broker published item change matcher scopes changes", BrokerPublishedItemChangeMatcherScopesChanges),
    ("Broker published item change matcher observes resolved writable targets", BrokerPublishedItemChangeMatcherObservesResolvedWritableTargets),
    ("Broker publisher sends value update for unregistered value change", BrokerPublisherSendsValueUpdateForUnregisteredValueChange),
    ("Broker publisher sends repeated write property command", BrokerPublisherSendsRepeatedWritePropertyCommand),
    ("Broker publisher records local host write state for writable value changes", BrokerPublisherRecordsLocalHostWriteStateForWritableValueChanges),
    ("Broker publisher omits write properties from retained snapshots", BrokerPublisherOmitsWritePropertiesFromRetainedSnapshots),
    ("Broker publisher skips child items and non-MQTT snapshot properties", BrokerPublisherSkipsChildItemsAndNonMqttSnapshotProperties),
    ("Broker write-back ignores non-writable entries", BrokerWriteBackIgnoresNonWritableEntries),
    ("Broker write-back ignores inactive entries", BrokerWriteBackIgnoresInactiveEntries),
    ("Broker write-back updates writable value", BrokerWriteBackUpdatesWritableValue),
    ("Broker write-back applies write requests", BrokerWriteBackAppliesWriteRequests),
    ("Broker write-back ignores own write request echo once", BrokerWriteBackIgnoresOwnWriteRequestEchoOnce),
    ("Broker write-back notifies repeated write requests", BrokerWriteBackNotifiesRepeatedWriteRequests),
    ("Broker write-back ignores property-style write state", BrokerWriteBackIgnoresPropertyStyleWriteState),
    ("Broker write-back ignores stale read after recent local host write", BrokerWriteBackIgnoresStaleReadAfterRecentLocalHostWrite),
    ("Broker write-back ignores stale read after recent local host write on resolved target", BrokerWriteBackIgnoresStaleReadAfterRecentLocalHostWriteOnResolvedTarget),
    ("Broker write-back applies uncached source write requests", BrokerWriteBackAppliesUncachedSourceWriteRequests),
    ("Broker write-back keeps external write requests enabled after recent local host write", BrokerWriteBackKeepsExternalWriteRequestsEnabledAfterRecentLocalHostWrite),
    ("Broker write-back treats same-value resolved target echoes as non-conflicts", BrokerWriteBackTreatsSameValueResolvedTargetEchoesAsNonConflicts),
    ("Broker write-back normalizes legacy request mode", BrokerWriteBackNormalizesLegacyRequestMode),
    ("Broker write-back converts numeric value to local type", BrokerWriteBackConvertsNumericValueToLocalType),
    ("Broker write-back blocks protected properties", BrokerWriteBackBlocksProtectedProperties),
    ("Broker write-back ignores same-value self echoes", BrokerWriteBackIgnoresSameValueSelfEchoes),
    ("Broker write-back cleanup disposes subscriptions", BrokerWriteBackCleanupDisposesSubscriptions),
    ("ItemModel exposure codec roundtrip", ItemExposureCodecRoundtrip),
    ("ItemModel exposure codec upsert and remove", ItemExposureCodecUpsertAndRemove),
    ("ItemModel exposure codec normalizes runtime broker paths", ItemExposureCodecNormalizesRuntimeBrokerPaths),
    ("ItemModel exposure publisher applies bit helpers", ItemExposurePublisherAppliesBitHelpers),
    ("Target property options hide protected properties", TargetPropertyOptionsHideProtectedProperties),
    ("Target property field hidden for normal widgets", TargetPropertyFieldHiddenForNormalWidgets),
    ("Target property defaults to value", TargetPropertyDefaultsToValue),
    ("Target property protected fallback uses value", TargetPropertyProtectedFallbackUsesValue),
    ("Signal write emits registry value update", SignalWriteEmitsRegistryValueUpdate),
    ("Signal write updates write property when present", SignalWriteUpdatesWritePropertyWhenPresent),
    ("Signal target property resolves value reference runtime value", SignalTargetPropertyResolvesValueReferenceRuntimeValue),
    ("Signal public read update preserves value reference resolution", SignalPublicReadUpdatePreservesValueReferenceResolution),
    ("Signal target property falls back when value reference is invalid", SignalTargetPropertyFallsBackWhenValueReferenceIsInvalid),
    ("Signal write keeps public writeback with value reference", SignalWriteKeepsPublicWritebackWithValueReference),
    ("Signal live value store resolves UDL value reference", SignalLiveValueStoreResolvesUdlValueReference),
    ("Signal live value scheduler applies latest wins", SignalLiveValueSchedulerAppliesLatestWins),
    ("Signal ancestor read property change uses value-only refresh", SignalAncestorReadPropertyChangeUsesValueOnlyRefresh),
    ("Signal ancestor read property queue stores child target", SignalAncestorReadPropertyQueueStoresChildTarget),
    ("Signal source options include descendants and skip status roots", SignalSourceOptionsIncludeDescendantsAndSkipStatusRoots),
};

var failures = new List<string>();
var testFilter = Environment.GetEnvironmentVariable("HORNET_EDITOR_TEST_FILTER");
var selectedTests = string.IsNullOrWhiteSpace(testFilter)
    ? tests
    : tests
        .Where(test => test.Name.Contains(testFilter, StringComparison.OrdinalIgnoreCase))
        .ToArray();

foreach (var test in selectedTests)
{
    try
    {
        test.Run();
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Editor tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine($"Editor tests passed: {selectedTests.Length}");
return 0;

static void CustomSignalCodecParsesYamlStyleNodes()
{
        var raw = """
[
    {
        "name": "DummyValue",
        "mode": "Input",
        "dataType": "Number",
        "isWritable": true,
        "writePath": "",
        "writeMode": "Direct",
        "unit": "",
        "format": "",
        "valueText": "123",
        "formula": "",
        "trigger": "OnSourceChange",
        "triggerIntervalSeconds": 1,
        "variables": [],
        "operation": "Copy",
        "sourcePath": "",
        "sourcePath2": "",
        "sourcePath3": ""
    },
    {
        "name": 42
    }
]
""";

        var parsed = CustomSignalDefinitionCodec.ParseDefinitions(raw);
        AssertEqual(1, parsed.Count);
        AssertEqual("DummyValue", parsed[0].Name);
        AssertEqual("123", parsed[0].ValueText);
        AssertEqual(CustomSignalMode.Input, parsed[0].Mode);
}

static void PathIdentityValidationAcceptsOnlySnakeCase()
{
    var helperType = typeof(MainWindowViewModel).Assembly.GetType("HornetStudio.Editor.Helpers.TargetPathHelper");
    if (helperType is null)
    {
        throw new InvalidOperationException("TargetPathHelper was not found.");
    }

    var method = helperType.GetMethod("IsValidPathIdentityName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("IsValidPathIdentityName was not found.");
    }

    AssertEqual(true, method.Invoke(null, ["custom_signal_1"]));
    AssertEqual(true, method.Invoke(null, ["signal1"]));
    AssertEqual(false, method.Invoke(null, ["CustomSignal1"]));
    AssertEqual(false, method.Invoke(null, ["custom-signal-1"]));
    AssertEqual(false, method.Invoke(null, ["_signal1"]));
    AssertEqual(false, method.Invoke(null, ["1signal"]));
}

static void FolderIdentityValidationAcceptsOnlySnakeCase()
{
    var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("TryValidateFolderIdentityName", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("TryValidateFolderIdentityName was not found.");
    }

    var arguments = new object?[] { "main", null, null };
    AssertEqual(true, method.Invoke(null, arguments));
    AssertEqual("main", arguments[1]);

    arguments = ["page_1", null, null];
    AssertEqual(true, method.Invoke(null, arguments));
    AssertEqual("page_1", arguments[1]);

    arguments = ["Folder1", null, null];
    AssertEqual(false, method.Invoke(null, arguments));
    AssertTrue(((string)arguments[2]!).Contains("snake_case", StringComparison.Ordinal));

    arguments = ["main-page", null, null];
    AssertEqual(false, method.Invoke(null, arguments));
    AssertTrue(((string)arguments[2]!).Contains("snake_case", StringComparison.Ordinal));
}

static void FolderIdentityDefaultsUseSnakeCase()
{
    var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("GetDefaultFolderIdentityName", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetDefaultFolderIdentityName was not found.");
    }

    AssertEqual("folder_1", method.Invoke(null, [1]));
    AssertEqual("folder_2", method.Invoke(null, [2]));
}

static void WorkflowCodecParsesYamlSteps()
{
    var raw = """
name: startup_sequence
steps:
  - type: Log
    targetLog: Logs.process
    level: Warning
    text: Starting
  - type: SetValue
    target: studio.main.pump.enable
    value: true
  - type: IfThenElse
    condition: temperature > 20
    then:
      - type: Delay
        milliseconds: 50
    else:
      - type: Log
        targetLog: Logs.audit
        text: Below limit
""";

    AssertTrue(FunctionDefinitionCodec.TryParse(raw, "startup_sequence.yaml", out var definition, out var validation));
    AssertTrue(validation.IsValid);
    AssertTrue(definition is not null);
    AssertEqual("startup_sequence", definition!.Name);
    AssertEqual(3, definition.Steps.Count);
    AssertEqual(FunctionStepType.Log, definition.Steps[0].Type);
    AssertEqual(FunctionStepType.SetValue, definition.Steps[1].Type);
    AssertEqual(FunctionStepType.IfThenElse, definition.Steps[2].Type);

    var conditional = (FunctionIfThenElseStepDefinition)definition.Steps[2];
    AssertEqual("temperature > 20", conditional.Condition);
    AssertEqual(1, conditional.Then.Count);
    AssertEqual(1, conditional.Else.Count);
}

static void WorkflowCodecRejectsMissingName()
{
    var raw = """
steps:
  - type: Delay
    milliseconds: 1
""";

    AssertFalse(FunctionDefinitionCodec.TryParse(raw, "invalid.yaml", out _, out var validation));
    AssertFalse(validation.IsValid);
    AssertTrue(validation.Errors.Any(error => error.Path == "function.name"));
}

static void WorkflowCodecParsesIfThenElseVariables()
{
    var raw = string.Join(
        "\n",
        [
            "name: conditional_startup",
            "steps:",
            "  - type: IfThenElse",
            "    condition: \"{A} == true\"",
            "    variables:",
            "      - name: A",
            "        sourcePath: custom_signals_1.ready",
            "    then:",
            "      - type: Log",
            "        targetLog: Logs.process",
            "        text: Ready"
        ]);

    AssertTrue(FunctionDefinitionCodec.TryParse(raw, "conditional_startup.yaml", out var definition, out var validation));
    AssertTrue(validation.IsValid);
    AssertTrue(definition is not null);

    AssertTrue(definition!.Steps.Single() is FunctionIfThenElseStepDefinition);
    var conditional = (FunctionIfThenElseStepDefinition)definition.Steps.Single();
    AssertEqual(1, conditional.Variables.Count);
    AssertEqual("A", conditional.Variables[0].Name);
    AssertEqual("custom_signals_1.ready", conditional.Variables[0].SourcePath);

    var serialized = FunctionDefinitionCodec.Serialize(definition);
    AssertTrue(serialized.Contains("variables:", StringComparison.Ordinal));
    AssertTrue(serialized.Contains("sourcePath: custom_signals_1.ready", StringComparison.Ordinal));
}

static void WorkflowCodecParsesWhile()
{
    var raw = string.Join(
        "\n",
        [
            "name: while_test",
            "steps:",
            "  - type: While",
            "    condition: \"{Enabled} == true\"",
            "    variables:",
            "      - name: Enabled",
            "        sourcePath: custom_signals_1.enabled",
            "    steps:",
            "      - type: Delay",
            "        milliseconds: 100",
            "      - type: Log",
            "        targetLog: Logs.process",
            "        text: Tick"
        ]);

    AssertTrue(FunctionDefinitionCodec.TryParse(raw, "while_test.yaml", out var definition, out var validation));
    AssertTrue(validation.IsValid);
    AssertTrue(definition is not null);
    AssertEqual(1, definition!.Steps.Count);
    AssertTrue(definition.Steps[0] is FunctionWhileStepDefinition);

    var loop = (FunctionWhileStepDefinition)definition.Steps[0];
    AssertEqual("{Enabled} == true", loop.Condition);
    AssertEqual(1, loop.Variables.Count);
    AssertEqual("Enabled", loop.Variables[0].Name);
    AssertEqual("custom_signals_1.enabled", loop.Variables[0].SourcePath);
    AssertEqual(2, loop.Steps.Count);
    AssertEqual(FunctionStepType.Delay, loop.Steps[0].Type);
    AssertEqual(FunctionStepType.Log, loop.Steps[1].Type);

    var serialized = FunctionDefinitionCodec.Serialize(definition);
    AssertTrue(serialized.Contains("type: While", StringComparison.Ordinal));
    AssertTrue(serialized.Contains("milliseconds: 100", StringComparison.Ordinal));
    AssertTrue(serialized.Contains("text: Tick", StringComparison.Ordinal));
}

static void WorkflowCodecRejectsWhileWithoutPositiveDelayGuard()
{
    var raw = string.Join(
        "\n",
        [
            "name: invalid_while",
            "steps:",
            "  - type: While",
            "    condition: enabled == true",
            "    steps:",
            "      - type: Log",
            "        targetLog: Logs.process",
            "        text: Busy"
        ]);

    AssertFalse(FunctionDefinitionCodec.TryParse(raw, "invalid_while.yaml", out _, out var validation));
    AssertFalse(validation.IsValid);
    AssertTrue(validation.Errors.Any(error => error.Message.Contains("positive Delay step", StringComparison.Ordinal)));
}

static void InteractionRuleCodecRoundtripPreservesRunFunction()
{
    var serialized = ItemInteractionRuleCodec.SerializeDefinitions(
    [
        new ItemInteractionRule
        {
            Event = ItemInteractionEvent.BodyLeftClick,
            Action = ItemInteractionAction.RunFunction,
            TargetPath = "this",
            FunctionName = "declarative:start_up",
            Argument = string.Empty
        }
    ]);

    AssertEqual("BodyLeftClick|RunFunction|this|declarative:start_up|", serialized);

    var parsed = ItemInteractionRuleCodec.ParseDefinitions(serialized);
    AssertEqual(1, parsed.Count);
    AssertEqual(ItemInteractionAction.RunFunction, parsed[0].Action);
    AssertEqual("this", parsed[0].TargetPath);
    AssertEqual("declarative:start_up", parsed[0].FunctionName);
    AssertEqual(string.Empty, parsed[0].Argument);
}

static void InteractionRuleCodecRoundtripPreservesStopFunction()
{
    var serialized = ItemInteractionRuleCodec.SerializeDefinitions(
    [
        new ItemInteractionRule
        {
            Event = ItemInteractionEvent.BodyRightClick,
            Action = ItemInteractionAction.StopFunction,
            TargetPath = "this",
            FunctionName = "declarative:loop_runner",
            Argument = string.Empty
        }
    ]);

    AssertEqual("BodyRightClick|StopFunction|this|declarative:loop_runner|", serialized);

    var parsed = ItemInteractionRuleCodec.ParseDefinitions(serialized);
    AssertEqual(1, parsed.Count);
    AssertEqual(ItemInteractionAction.StopFunction, parsed[0].Action);
    AssertEqual("this", parsed[0].TargetPath);
    AssertEqual("declarative:loop_runner", parsed[0].FunctionName);
}

static void SetValueOperationCodecRoundtripPreservesStructuredPayload()
{
    var serialized = SetValueOperationCodec.Serialize(new SetValueOperation
    {
        Kind = SetValueOperationKind.AppendText,
        LiteralValue = "tail",
        Separator = ", "
    });

    AssertTrue(serialized.StartsWith(SetValueOperationCodec.StructuredPrefix, StringComparison.Ordinal));

    var parsed = SetValueOperationCodec.Parse(serialized);
    AssertTrue(parsed.IsValid);
    AssertTrue(parsed.IsStructured);
    AssertEqual(SetValueOperationKind.AppendText, parsed.Operation.Kind);
    AssertEqual("tail", parsed.Operation.LiteralValue);
    AssertEqual(", ", parsed.Operation.Separator);
    AssertEqual(string.Empty, parsed.Operation.SourcePath);
    AssertFalse(parsed.Operation.IsLegacyLiteral);
}

static void SetValueOperationCodecKeepsLegacyLiteralFallback()
{
    var parsed = SetValueOperationCodec.Parse("123");

    AssertTrue(parsed.IsValid);
    AssertFalse(parsed.IsStructured);
    AssertEqual(SetValueOperationKind.SetLiteral, parsed.Operation.Kind);
    AssertEqual("123", parsed.Operation.LiteralValue);
    AssertTrue(parsed.Operation.IsLegacyLiteral);
    AssertEqual("Legacy literal 123", SetValueOperationCodec.GetSummary("123", SetValueTargetKind.Numeric));
}

static void SetValueOperationSummaryFormatsStructuredOperations()
{
    var setFromItem = SetValueOperationCodec.Serialize(new SetValueOperation
    {
        Kind = SetValueOperationKind.SetFromItem,
        SourcePath = "main/pump/value"
    });
    var appendText = SetValueOperationCodec.Serialize(new SetValueOperation
    {
        Kind = SetValueOperationKind.AppendText,
        LiteralValue = "rpm",
        Separator = " "
    });

    AssertEqual("Set from main.pump.value", SetValueOperationCodec.GetSummary(setFromItem, SetValueTargetKind.Numeric));
    AssertEqual("Append \"rpm\" with separator \" \"", SetValueOperationCodec.GetSummary(appendText, SetValueTargetKind.String));
}

static void SetValueOperationValidationRejectsUnsupportedNumericOperation()
{
    var validation = SetValueOperationCodec.Validate(
        new SetValueOperation
        {
            Kind = SetValueOperationKind.AppendText,
            LiteralValue = "text"
        },
        SetValueTargetKind.Numeric);

    AssertFalse(validation.IsValid);
    AssertEqual("This operation is not available for numeric targets.", validation.ErrorMessage);
}

static void TargetValueTypeParserNormalizesCanonicalValues()
{
    AssertEqual(TargetValueType.Unknown, TargetValueTypes.Parse("unknown"));
    AssertEqual(TargetValueType.String, TargetValueTypes.Parse("String"));
    AssertEqual(TargetValueType.Bool, TargetValueTypes.Parse("boolean"));
    AssertEqual(TargetValueType.Int, TargetValueTypes.Parse("int"));
    AssertEqual(TargetValueType.Long, TargetValueTypes.Parse("long"));
    AssertEqual(TargetValueType.Float, TargetValueTypes.Parse("single"));
    AssertEqual(TargetValueType.Double, TargetValueTypes.Parse("double"));
    AssertEqual(TargetValueType.Decimal, TargetValueTypes.Parse("decimal"));
    AssertEqual(TargetValueType.Epoch, TargetValueTypes.Parse("epoch"));
    AssertEqual(TargetValueType.Bits, TargetValueTypes.Parse("bitfield"));
    AssertEqual(TargetValueType.Object, TargetValueTypes.Parse("object"));
    AssertEqual(TargetValueType.Unknown, TargetValueTypes.Parse("unsupported"));

    AssertEqual(SetValueTargetKind.Numeric, TargetValueTypes.ToSetValueTargetKind(TargetValueType.Int));
    AssertEqual(SetValueTargetKind.Numeric, TargetValueTypes.ToSetValueTargetKind(TargetValueType.Epoch));
    AssertEqual(SetValueTargetKind.Numeric, TargetValueTypes.ToSetValueTargetKind(TargetValueType.Bits));
    AssertEqual(SetValueTargetKind.Boolean, TargetValueTypes.ToSetValueTargetKind(TargetValueType.Bool));
    AssertEqual(SetValueTargetKind.String, TargetValueTypes.ToSetValueTargetKind(TargetValueType.String));
    AssertEqual(SetValueTargetKind.Unknown, TargetValueTypes.ToSetValueTargetKind(TargetValueType.Object));
}

static void SetValueTargetClassificationPrefersExplicitTypeOverEmptyValue()
{
    AssertEqual(SetValueTargetKind.Numeric, SetValueOperationCodec.ClassifyTargetKind("float", targetType: null, sampleValue: null));
    AssertEqual(SetValueTargetKind.Numeric, SetValueOperationCodec.ClassifyTargetKind("epoch", targetType: typeof(string), sampleValue: string.Empty));
    AssertEqual(SetValueTargetKind.Numeric, SetValueOperationCodec.ClassifyTargetKind("bits", targetType: null, sampleValue: null));
    AssertEqual(SetValueTargetKind.Boolean, SetValueOperationCodec.ClassifyTargetKind("bool", targetType: null, sampleValue: null));
    AssertEqual(SetValueTargetKind.String, SetValueOperationCodec.ClassifyTargetKind("string", targetType: null, sampleValue: null));
    AssertEqual(SetValueTargetKind.Unknown, SetValueOperationCodec.ClassifyTargetKind("object", targetType: null, sampleValue: null));
    AssertEqual(SetValueTargetKind.Numeric, SetValueOperationCodec.ClassifyTargetKind(declaredType: null, targetType: typeof(double), sampleValue: null));
}

static void SetValueDescriptorResolvesNumericKindForRowTargetPath()
{
    const string targetPath = "udl1.m001.set";
    var item = ItemExtension.CreateWithPath(targetPath, 1.25f);
    item.Properties["write"].Value = 1.25f;
    item.Properties["type"].Value = "float";
    item.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, item);

    try
    {
        var field = CreateInteractionRuleField();

        var descriptor = field.GetSetValueTargetDescriptor(targetPath);

        AssertEqual(targetPath, descriptor.TargetPath);
        AssertEqual(SetValueTargetKind.Numeric, descriptor.TargetKind);
        AssertEqual(true, descriptor.IsWritable);
        AssertEqual("write", descriptor.ValuePropertyName);
    }
    finally
    {
        HostRegistries.Data.Remove(targetPath);
    }
}

static void SetValueDescriptorPreservesUnresolvedRequestedRowTargetPath()
{
    var field = CreateInteractionRuleField();

    var descriptor = field.GetSetValueTargetDescriptor("udl1.m001.set");

    AssertEqual("udl1.m001.set", descriptor.TargetPath);
    AssertEqual(SetValueTargetKind.Unknown, descriptor.TargetKind);
    AssertEqual(true, descriptor.IsWritable);
}

static void SetValueSourceOptionsAllowReadonlyCompatibleFloatSource()
{
    const string targetPath = "udl1.m001.set";
    const string sourcePath = "custom_signals_1.input_1";
    var targetItem = ItemExtension.CreateWithPath(targetPath, 0f);
    targetItem.Properties["write"].Value = 0f;
    targetItem.Properties["type"].Value = "float";
    targetItem.Properties["writable"].Value = true;
    var sourceItem = ItemExtension.CreateWithPath(sourcePath, 0f);
    sourceItem.Properties["read"].Value = 0f;
    sourceItem.Properties["type"].Value = "float";
    sourceItem.Properties["writable"].Value = false;
    HostRegistries.Data.UpsertSnapshot(targetPath, targetItem);
    HostRegistries.Data.UpsertSnapshot(sourcePath, sourceItem);

    try
    {
        var field = CreateInteractionRuleField();
        field.InteractionTargetOptions.Add(targetPath);
        field.InteractionTargetOptions.Add(sourcePath);

        var sourceOptions = field.GetCompatibleSetValueSourceOptions(targetPath);

        AssertTrue(sourceOptions.Contains(sourcePath, StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(targetPath);
        HostRegistries.Data.Remove(sourcePath);
    }
}

static void SetValueSourceValidationResolvesCompatibleFloatSourceDirectly()
{
    const string targetPath = "udl1.m001.set";
    const string sourcePath = "custom_signals_1.input_1";
    var targetItem = ItemExtension.CreateWithPath(targetPath, 0f);
    targetItem.Properties["write"].Value = 0f;
    targetItem.Properties["type"].Value = "float";
    targetItem.Properties["writable"].Value = true;
    var sourceItem = ItemExtension.CreateWithPath(sourcePath, 0f);
    sourceItem.Properties["read"].Value = 0f;
    sourceItem.Properties["type"].Value = "float";
    sourceItem.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, targetItem);
    HostRegistries.Data.UpsertSnapshot(sourcePath, sourceItem);

    try
    {
        var field = CreateInteractionRuleField();

        var isCompatible = field.IsCompatibleSetValueSourcePath(targetPath, sourcePath);

        AssertTrue(isCompatible);
    }
    finally
    {
        HostRegistries.Data.Remove(targetPath);
        HostRegistries.Data.Remove(sourcePath);
    }
}

static void WorkflowExecutorResolvesStepLocalConditionVariables()
{
    var executedLogs = new List<string>();
    var definition = new FunctionDefinition
    {
        Name = "conditional_startup",
        Steps =
        [
            new FunctionIfThenElseStepDefinition
            {
                Condition = "{A} == true && {GlobalFlag} == true",
                Variables =
                [
                    new BooleanConditionVariableDefinition
                    {
                        Name = "A",
                        SourcePath = "custom_signals_1.ready"
                    }
                ],
                Then =
                [
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.process",
                        Text = "then"
                    }
                ],
                Else =
                [
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.process",
                        Text = "else"
                    }
                ]
            }
        ]
    };

    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                ConditionVariables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GlobalFlag"] = true
                },
                ResolveConditionSourceValueAsync = (sourcePath, _) => ValueTask.FromResult(
                    string.Equals(sourcePath, "custom_signals_1.ready", StringComparison.Ordinal)
                        ? new FunctionConditionVariableResolutionResult(true, true)
                        : new FunctionConditionVariableResolutionResult(false, null)),
                WriteLogAsync = (step, _) =>
                {
                    executedLogs.Add(step.Text);
                    return ValueTask.CompletedTask;
                }
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Done, result.State);
    AssertEqual(1, executedLogs.Count);
    AssertEqual("then", executedLogs[0]);
}

static void WorkflowExecutorFailsMissingConditionVariableSource()
{
    var definition = new FunctionDefinition
    {
        Name = "conditional_startup",
        Steps =
        [
            new FunctionIfThenElseStepDefinition
            {
                Condition = "{A} == true",
                Variables =
                [
                    new BooleanConditionVariableDefinition
                    {
                        Name = "A",
                        SourcePath = "custom_signals_1.missing"
                    }
                ],
                Then =
                [
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.process",
                        Text = "then"
                    }
                ]
            }
        ]
    };

    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                ResolveConditionSourceValueAsync = (_, _) => ValueTask.FromResult(new FunctionConditionVariableResolutionResult(false, null)),
                WriteLogAsync = static (_, _) => ValueTask.CompletedTask
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Failed, result.State);
    AssertTrue(result.ErrorMessage.Contains("could not be resolved", StringComparison.Ordinal));
}

static void WorkflowExecutorRunsWhileUntilConditionBecomesFalse()
{
    var executedLogs = new List<string>();
    var evaluations = 0;
    var definition = new FunctionDefinition
    {
        Name = "loop_test",
        Steps =
        [
            new FunctionWhileStepDefinition
            {
                Condition = "loop == true",
                Steps =
                [
                    new FunctionDelayStepDefinition
                    {
                        Milliseconds = 1
                    },
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.process",
                        Text = "tick"
                    }
                ]
            }
        ]
    };

    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                EvaluateConditionAsync = (_, _, _) => ValueTask.FromResult(evaluations++ < 2),
                WriteLogAsync = (step, _) =>
                {
                    executedLogs.Add(step.Text);
                    return ValueTask.CompletedTask;
                }
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Done, result.State);
    AssertEqual(2, executedLogs.Count);
    AssertEqual("tick", executedLogs[0]);
    AssertEqual("tick", executedLogs[1]);
}

static void WorkflowExecutorControlledStopCompletesAsDone()
{
    var executedLogs = new List<string>();
    var stopController = new FunctionExecutionStopController();
    var definition = new FunctionDefinition
    {
        Name = "controlled_stop_test",
        Steps =
        [
            new FunctionWhileStepDefinition
            {
                Condition = "true",
                Steps =
                [
                    new FunctionDelayStepDefinition
                    {
                        Milliseconds = 1
                    },
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.process",
                        Text = "tick"
                    }
                ]
            }
        ]
    };

    var stopRequested = false;
    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                StopController = stopController,
                WriteLogAsync = (step, _) =>
                {
                    executedLogs.Add(step.Text);

                    if (!stopRequested)
                    {
                        stopRequested = true;
                        stopController.RequestStop();
                    }

                    return ValueTask.CompletedTask;
                }
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Done, result.State);
    AssertEqual(1, executedLogs.Count);
    AssertEqual("tick", executedLogs[0]);
}

static void FunctionRegistryCreatesDeclarativeEntries()
{
    var rootDirectory = CreateTempDirectory();
    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        var legacyDirectory = Path.Combine(rootDirectory, "Scripts", "Workflows");
        Directory.CreateDirectory(legacyDirectory);

        File.WriteAllText(Path.Combine(functionsDirectory, "alpha.yaml"), """
name: Alpha Sequence
steps:
  - type: Delay
    milliseconds: 10
""");

        File.WriteAllText(Path.Combine(legacyDirectory, "alpha.yaml"), """
name: Legacy Alpha
steps:
  - type: Delay
    milliseconds: 20
""");

        File.WriteAllText(Path.Combine(legacyDirectory, "beta.yaml"), """
name: Beta Sequence
steps:
  - type: Log
    targetLog: Logs.process
    text: Ready
""");

        var entries = FunctionRegistry.EnumerateEntries(rootDirectory);
        AssertEqual(2, entries.Count);

        var alphaEntry = entries.Single(entry => entry.Reference == "yaml:alpha");
        AssertEqual("Alpha Sequence", alphaEntry.Name);
        AssertEqual(FunctionCatalogKind.Declarative, alphaEntry.Kind);
        AssertEqual(FunctionCatalogSource.FunctionsDirectory, alphaEntry.Source);
        AssertEqual("Scripts/Functions", alphaEntry.DisplaySource);
        AssertEqual(true, alphaEntry.CanEdit);
        AssertEqual(true, alphaEntry.CanDelete);
        AssertEqual(true, alphaEntry.CanRun);
        AssertEqual(true, alphaEntry.IsValid);

        var betaEntry = entries.Single(entry => entry.Reference == "yaml:beta");
        AssertEqual(FunctionCatalogSource.LegacyWorkflowDirectory, betaEntry.Source);
        AssertEqual("Scripts/Workflows (legacy)", betaEntry.DisplaySource);
    }
    finally
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void FunctionRegistrySurfacesInvalidDeclarativeFiles()
{
    var rootDirectory = CreateTempDirectory();
    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        File.WriteAllText(Path.Combine(functionsDirectory, "broken.yaml"), "steps: []");

        var entries = FunctionRegistry.EnumerateEntries(rootDirectory);
        AssertEqual(1, entries.Count);

        var brokenEntry = entries[0];
        AssertEqual("yaml:broken", brokenEntry.Reference);
        AssertEqual("broken", brokenEntry.Name);
        AssertEqual(false, brokenEntry.IsValid);
        AssertEqual(true, brokenEntry.CanEdit);
        AssertEqual(true, brokenEntry.CanDelete);
        AssertEqual(false, brokenEntry.CanRun);
        AssertTrue(!string.IsNullOrWhiteSpace(brokenEntry.StatusText));
    }
    finally
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void FunctionRegistryCombinesDeclarativeAndPythonEntries()
{
    var rootDirectory = CreateTempDirectory();
    const string targetPath = "interaction:demo.owner:runtime";
    var client = CreatePythonClientForRegistryTests("demo_client", ["send_status", "send_status", "  ping  "]);

    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        File.WriteAllText(Path.Combine(functionsDirectory, "alpha.yaml"), """
name: Alpha Sequence
steps:
  - type: Delay
    milliseconds: 10
""");

        PythonClientRuntimeRegistry.Register(targetPath, "Demo Client", client);

        var entries = FunctionRegistry.EnumerateEntries(rootDirectory);
        AssertEqual(3, entries.Count);

        var alphaEntry = entries.Single(entry => entry.Reference == "yaml:alpha");
        AssertEqual(FunctionCatalogKind.Declarative, alphaEntry.Kind);

        var pingEntry = entries.Single(entry => entry.Reference == "python:interaction:demo.owner:runtime:ping");
        AssertEqual("ping", pingEntry.Name);
        AssertEqual(FunctionCatalogKind.Python, pingEntry.Kind);
        AssertEqual(FunctionCatalogSource.PythonApplication, pingEntry.Source);
        AssertEqual(targetPath, pingEntry.SourceIdentifier);
        AssertEqual("Applications/Python", pingEntry.DisplaySource);
        AssertEqual(false, pingEntry.CanEdit);
        AssertEqual(false, pingEntry.CanDelete);
        AssertEqual(true, pingEntry.CanRun);
        AssertEqual(true, pingEntry.IsValid);

        var statusEntry = entries.Single(entry => entry.Reference == "python:interaction:demo.owner:runtime:send_status");
        AssertEqual("send_status", statusEntry.Name);
    }
    finally
    {
        PythonClientRuntimeRegistry.Unregister(targetPath);

        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void FunctionRegistryResolvesStableReferences()
{
    var rootDirectory = CreateTempDirectory();
    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        File.WriteAllText(Path.Combine(functionsDirectory, "alpha.yaml"), """
name: Alpha Sequence
steps:
  - type: Delay
    milliseconds: 10
""");

        AssertTrue(FunctionRegistry.TryGetEntry(rootDirectory, "declarative:alpha", out var entry));
        AssertTrue(entry is not null);
        AssertEqual("Alpha Sequence", entry!.Name);
        AssertEqual(FunctionCatalogKind.Declarative, entry.Kind);
        AssertEqual(true, entry.CanRun);
        AssertEqual("yaml:alpha", entry.Reference);
    }
    finally
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void FunctionRegistryResolvesPythonStableReferences()
{
    var rootDirectory = CreateTempDirectory();
    const string targetPath = "interaction:demo.folder:python";
    var client = CreatePythonClientForRegistryTests("python_lookup", ["status_report"]);

    try
    {
        PythonClientRuntimeRegistry.Register(targetPath, "Python Lookup", client);

        AssertTrue(FunctionRegistry.TryGetEntry(rootDirectory, "python:interaction:demo.folder:python:status_report", out var entry));
        AssertTrue(entry is not null);
        AssertEqual("status_report", entry!.Name);
        AssertEqual(FunctionCatalogKind.Python, entry.Kind);
        AssertEqual(true, entry.CanRun);
    }
    finally
    {
        PythonClientRuntimeRegistry.Unregister(targetPath);

        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void RunFunctionOptionsIncludeRunnablePythonEntries()
{
    var rootDirectory = CreateTempDirectory();
    const string targetPath = "interaction:demo.folder:python";
    var client = CreatePythonClientForRegistryTests("python_runfunction", ["status_report"]);

    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        File.WriteAllText(Path.Combine(functionsDirectory, "alpha.yaml"), """
name: Alpha Sequence
steps:
  - type: Delay
    milliseconds: 10
""");

        PythonClientRuntimeRegistry.Register(targetPath, "Python RunFunction", client);

        var definition = new EditorDialogBindingDefinition(
            "InteractionRules",
            "Interaction Rules",
            EditorPropertyType.InteractionRuleList,
            static _ => string.Empty);
        var field = new EditorDialogField(definition, new ItemProperty("InteractionRules", string.Empty, "page.button.InteractionRules"));
        var workspaceDirectoryProperty = typeof(EditorDialogField).GetProperty(
            nameof(EditorDialogField.OwnerWorkspaceDirectory),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("OwnerWorkspaceDirectory property was not found.");
        workspaceDirectoryProperty.SetValue(field, rootDirectory);

        var options = field.GetInteractionFunctionOptions(nameof(ItemInteractionAction.RunFunction), "this");
        AssertEqual(2, options.Count);
        AssertTrue(options.Contains("yaml:alpha"));
        AssertTrue(options.Contains("python:interaction:demo.folder:python:status_report"));
    }
    finally
    {
        PythonClientRuntimeRegistry.Unregister(targetPath);

        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void FunctionRegistryResolvesYamlAlias()
{
    var rootDirectory = CreateTempDirectory();
    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        File.WriteAllText(Path.Combine(functionsDirectory, "alpha.yaml"), """
name: Alpha Sequence
steps:
  - type: Delay
    milliseconds: 10
""");

        AssertTrue(FunctionRegistry.TryGetEntry(rootDirectory, "yaml:alpha", out var yamlEntry));
        AssertTrue(yamlEntry is not null);
        AssertEqual("yaml:alpha", yamlEntry!.Reference);

        AssertTrue(FunctionRegistry.TryGetEntry(rootDirectory, "declarative:alpha", out var declarativeEntry));
        AssertTrue(declarativeEntry is not null);
        AssertEqual("yaml:alpha", declarativeEntry!.Reference);
        AssertTrue(FunctionRegistry.ReferencesEqual("yaml:alpha", "declarative:alpha"));
    }
    finally
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void RunFunctionPickerUsesDisplayLabels()
{
    var rootDirectory = CreateTempDirectory();
    const string targetPath = "python-env:main.application_explorer_1:raw";
    var client = CreatePythonClientForRegistryTests("python_display", ["start_loop"]);

    try
    {
        var functionsDirectory = FunctionDefinitionCodec.GetFunctionDirectory(rootDirectory);
        Directory.CreateDirectory(functionsDirectory);
        File.WriteAllText(Path.Combine(functionsDirectory, "new_workflow.yaml"), """
name: New Workflow
steps:
  - type: Delay
    milliseconds: 10
""");

        PythonClientRuntimeRegistry.Register(targetPath, "Python Display", client);

        var definition = new EditorDialogBindingDefinition(
            "InteractionRules",
            "Interaction Rules",
            EditorPropertyType.InteractionRuleList,
            static _ => string.Empty);
        var field = new EditorDialogField(definition, new ItemProperty("InteractionRules", string.Empty, "page.button.InteractionRules"));
        var workspaceDirectoryProperty = typeof(EditorDialogField).GetProperty(
            nameof(EditorDialogField.OwnerWorkspaceDirectory),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("OwnerWorkspaceDirectory property was not found.");
        workspaceDirectoryProperty.SetValue(field, rootDirectory);

        var options = field.GetRunFunctionOptions();
        var yamlOption = options.Single(option => option.Reference == "yaml:new_workflow");
        AssertEqual("YAML / new_workflow", yamlOption.DisplayText);

        var pythonOption = options.Single(option => option.Reference == "python:python-env:main.application_explorer_1:raw:start_loop");
        AssertEqual("Python / application_explorer_1 / raw / start_loop", pythonOption.DisplayText);
    }
    finally
    {
        PythonClientRuntimeRegistry.Unregister(targetPath);

        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

static void RunFunctionPickerKeepsMissingReferenceVisible()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.RunFunction),
        FunctionName = "declarative:missing_function"
    };

    row.SetRunFunctionOptions(
    [
        new FunctionPickerOption
        {
            Reference = "yaml:existing_function",
            DisplayText = "YAML / existing_function"
        }
    ],
    row.FunctionName);

    AssertEqual(2, row.RunFunctionOptions.Count);
    AssertEqual("Missing / declarative:missing_function", row.RunFunctionOptions.Last().DisplayText);
    AssertEqual("declarative:missing_function", row.SelectedRunFunctionOption?.Reference);
}

static void RunFunctionEditorKeepsArgumentVisible()
{
    var definition = new EditorDialogBindingDefinition(
        "InteractionRules",
        "Interaction Rules",
        EditorPropertyType.InteractionRuleList,
        static _ => string.Empty);
    var field = new EditorDialogField(definition, new ItemProperty("InteractionRules", string.Empty, "page.button.InteractionRules"));
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.RunFunction),
        TargetPath = "this",
        Argument = "payload"
    };

    field.RefreshInteractionRuleRowOptions(row);

    AssertTrue(row.ShowsArgumentEditor);
    AssertEqual("payload", row.Argument);
}

static void StopFunctionEditorUsesRunFunctionPickerBehavior()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.StopFunction),
        FunctionName = "declarative:missing_function"
    };

    row.SetRunFunctionOptions(
    [
        new FunctionPickerOption
        {
            Reference = "yaml:existing_function",
            DisplayText = "YAML / existing_function"
        }
    ],
    row.FunctionName);

    AssertTrue(row.IsRunFunctionAction);
    AssertFalse(row.ShowsTargetSelection);
    AssertTrue(row.ShowsFunctionPicker);
    AssertEqual(2, row.RunFunctionOptions.Count);
    AssertEqual("Missing / declarative:missing_function", row.RunFunctionOptions.Last().DisplayText);
    AssertEqual("declarative:missing_function", row.SelectedRunFunctionOption?.Reference);
}

static string CreateTempDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), $"hornetstudio_editor_tests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    return directory;
}

static PythonClient CreatePythonClientForRegistryTests(string clientName, IReadOnlyList<string> functionNames)
{
    var client = new PythonClient(new PythonClientOptions
    {
        Name = clientName,
        ClientType = "test",
        ScriptPath = Path.Combine(Path.GetTempPath(), $"{clientName}.py")
    });

    var functionsField = typeof(PythonClient).GetField("_functions", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PythonClient function registry field was not found.");
    var functions = functionsField.GetValue(client) as System.Collections.Concurrent.ConcurrentDictionary<string, PythonFunctionInfo>
        ?? throw new InvalidOperationException("PythonClient function registry field has an unexpected type.");

    foreach (var functionName in functionNames)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            continue;
        }

        var normalizedName = functionName.Trim();
        functions[normalizedName] = new PythonFunctionInfo(normalizedName, string.Empty, string.Empty);
    }

    return client;
}

static void WorkflowEditorConverterCreatesEditableRows()
{
    var definition = new FunctionDefinition
    {
        Name = "editor_test",
        Steps =
        [
            new FunctionSetValueStepDefinition
            {
                Target = "studio.main.pump.enable",
                Value = "true"
            },
            new FunctionDelayStepDefinition
            {
                Milliseconds = 250
            },
            new FunctionLogStepDefinition
            {
                TargetLog = "Logs.process",
                Level = MonitorLogLevel.Warning,
                Text = "Started"
            }
        ]
    };

    var rows = FunctionEditorDefinitionConverter.CreateRows(definition);
    AssertEqual(3, rows.Count);
    AssertEqual(FunctionStepType.SetValue, rows[0].StepType);
    AssertEqual("studio.main.pump.enable", rows[0].Target);
    AssertEqual(FunctionStepType.Delay, rows[1].StepType);
    AssertEqual("250", rows[1].MillisecondsText);
    AssertEqual(FunctionStepType.Log, rows[2].StepType);
    AssertEqual("Logs.process", rows[2].TargetLog);
    AssertEqual(MonitorLogLevel.Warning.ToString(), rows[2].LevelText);
}

static void WorkflowEditorCreatesWhileDefaultDelayGuard()
{
    var row = FunctionStepEditorRow.CreateNew(FunctionStepType.While);

    AssertEqual(FunctionStepType.While, row.StepType);
    AssertEqual(1, row.WhileRows.Count);
    AssertEqual(FunctionStepType.Delay, row.WhileRows[0].StepType);
    AssertEqual("100", row.WhileRows[0].MillisecondsText);
    AssertTrue(row.WhileRows[0].RequiresPositiveDelay);
}

static void WorkflowEditorConverterEditsIfThenElseSteps()
{
    var definition = new FunctionDefinition
    {
        Name = "editor_preserve",
        Steps =
        [
            new FunctionLogStepDefinition
            {
                TargetLog = "Logs.process",
                Text = "Before"
            },
            new FunctionIfThenElseStepDefinition
            {
                Condition = "{A} > 20",
                Variables =
                [
                    new BooleanConditionVariableDefinition
                    {
                        Name = "A",
                        SourcePath = "custom_signals_1.temperature"
                    }
                ],
                Then =
                [
                    new FunctionDelayStepDefinition
                    {
                        Milliseconds = 25
                    }
                ],
                Else =
                [
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.audit",
                        Level = MonitorLogLevel.Error,
                        Text = "Too cold"
                    }
                ]
            },
            new FunctionSetValueStepDefinition
            {
                Target = "studio.main.pump.enable",
                Value = "false"
            }
        ]
    };

    var rows = FunctionEditorDefinitionConverter.CreateRows(definition).ToArray();
    AssertEqual(3, rows.Length);
    AssertFalse(rows[1].IsPreserved);
    AssertEqual(FunctionStepType.IfThenElse, rows[1].StepType);
    AssertEqual("{A} > 20", rows[1].ConditionEditor.FormulaText);
    AssertEqual(1, rows[1].ConditionEditor.Variables.Count);
    AssertEqual("A", rows[1].ConditionEditor.Variables[0].Name);
    AssertEqual(1, rows[1].ThenRows.Count);
    AssertEqual(1, rows[1].ElseRows.Count);

    rows[0].Text = "Changed";
    rows[1].ConditionEditor.FormulaText = "{A} >= 25";
    rows[1].ConditionEditor.Variables[0].SourcePath = "custom_signals_1.temperature_filtered";
    rows[1].ThenRows.Add(FunctionStepEditorRow.CreateNew(FunctionStepType.Log));
    rows[1].ThenRows[1].TargetLog = "Logs.audit";
    rows[1].ThenRows[1].Text = "Then 2";
    rows[2].Value = "true";

    AssertTrue(FunctionEditorDefinitionConverter.TryBuildDefinition("editor_preserve", rows, out var rebuilt, out var errorMessage));
    AssertEqual(string.Empty, errorMessage);
    AssertTrue(rebuilt is not null);
    AssertEqual(3, rebuilt!.Steps.Count);
    AssertEqual(FunctionStepType.IfThenElse, rebuilt.Steps[1].Type);

    var conditional = (FunctionIfThenElseStepDefinition)rebuilt.Steps[1];
    AssertEqual("{A} >= 25", conditional.Condition);
    AssertEqual(1, conditional.Variables.Count);
    AssertEqual("custom_signals_1.temperature_filtered", conditional.Variables[0].SourcePath);
    AssertEqual(2, conditional.Then.Count);
    AssertEqual(1, conditional.Else.Count);
    AssertEqual(25, ((FunctionDelayStepDefinition)conditional.Then[0]).Milliseconds);
    AssertEqual("Then 2", ((FunctionLogStepDefinition)conditional.Then[1]).Text);
    AssertEqual("Too cold", ((FunctionLogStepDefinition)conditional.Else[0]).Text);
    AssertEqual("Changed", ((FunctionLogStepDefinition)rebuilt.Steps[0]).Text);
    AssertEqual("true", ((FunctionSetValueStepDefinition)rebuilt.Steps[2]).Value);
}

static void WorkflowEditorConverterEditsWhileSteps()
{
    var definition = new FunctionDefinition
    {
        Name = "editor_while",
        Steps =
        [
            new FunctionWhileStepDefinition
            {
                Condition = "{Enabled} == true",
                Variables =
                [
                    new BooleanConditionVariableDefinition
                    {
                        Name = "Enabled",
                        SourcePath = "custom_signals_1.enabled"
                    }
                ],
                Steps =
                [
                    new FunctionDelayStepDefinition
                    {
                        Milliseconds = 100
                    },
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.audit",
                        Text = "initial"
                    }
                ]
            }
        ]
    };

    var rows = FunctionEditorDefinitionConverter.CreateRows(definition).ToArray();
    AssertEqual(1, rows.Length);
    AssertEqual(FunctionStepType.While, rows[0].StepType);
    AssertEqual("{Enabled} == true", rows[0].ConditionEditor.FormulaText);
    AssertEqual(2, rows[0].WhileRows.Count);
    AssertTrue(rows[0].WhileRows[0].RequiresPositiveDelay);
    AssertEqual(FunctionStepType.Log, rows[0].WhileRows[1].StepType);

    rows[0].ConditionEditor.FormulaText = "{Enabled} != false";
    rows[0].WhileRows.Move(0, 1);
    rows[0].WhileRows[1].MillisecondsText = "25";
    rows[0].WhileRows[0].Text = "initial-updated";
    rows[0].WhileRows.Add(FunctionStepEditorRow.CreateNew(FunctionStepType.Log));
    rows[0].WhileRows[2].TargetLog = "Logs.process";
    rows[0].WhileRows[2].Text = "tail";

    AssertTrue(FunctionEditorDefinitionConverter.TryBuildDefinition("editor_while", rows, out var rebuilt, out var errorMessage));
    AssertEqual(string.Empty, errorMessage);
    AssertTrue(rebuilt is not null);
    AssertTrue(rebuilt!.Steps[0] is FunctionWhileStepDefinition);

    var loop = (FunctionWhileStepDefinition)rebuilt.Steps[0];
    AssertEqual("{Enabled} != false", loop.Condition);
    AssertEqual(3, loop.Steps.Count);
    AssertEqual(FunctionStepType.Log, loop.Steps[0].Type);
    AssertEqual("initial-updated", ((FunctionLogStepDefinition)loop.Steps[0]).Text);
    AssertEqual(FunctionStepType.Delay, loop.Steps[1].Type);
    AssertEqual(25, ((FunctionDelayStepDefinition)loop.Steps[1]).Milliseconds);
    AssertEqual(FunctionStepType.Log, loop.Steps[2].Type);
    AssertEqual("tail", ((FunctionLogStepDefinition)loop.Steps[2]).Text);
}

static void BooleanConditionEditorAddsVariables()
{
    var editor = new HornetStudio.Editor.Widgets.Common.BooleanConditionEditorViewModel();

    editor.AddVariable();
    editor.AddVariable();

    AssertEqual(2, editor.Variables.Count);
    AssertEqual("A", editor.Variables[0].Name);
    AssertEqual("B", editor.Variables[1].Name);
    AssertEqual(2, editor.VariableButtons.Count);
    AssertEqual("{A}", editor.VariableButtons[0].Token);
    AssertEqual("{B}", editor.VariableButtons[1].Token);
}

static void WorkflowRowConditionEditingCommitsClonedState()
{
    var row = FunctionStepEditorRow.FromStep(
        new FunctionIfThenElseStepDefinition
        {
            Condition = "{A} > 20",
            Variables =
            [
                new BooleanConditionVariableDefinition
                {
                    Name = "A",
                    SourcePath = "custom_signals_1.temperature"
                }
            ],
            Then =
            [
                new FunctionDelayStepDefinition
                {
                    Milliseconds = 100
                }
            ]
        });

    var clone = row.CreateConditionEditorClone();
    clone.FormulaText = "{B} >= 25";
    clone.Variables.Clear();
    clone.AddVariable("B", "custom_signals_1.temperature_filtered");

    AssertEqual("{A} > 20", row.ConditionEditor.FormulaText);
    AssertEqual("Condition", row.ConditionButtonText);
    AssertEqual("Condition: {A} > 20", row.ConditionSummary);

    AssertTrue(clone.TryBuildVariables(out var committedVariables, out var errorMessage));
    AssertEqual(string.Empty, errorMessage);

    row.ApplyCondition(clone.FormulaText, committedVariables);

    AssertEqual("{B} >= 25", row.ConditionEditor.FormulaText);
    AssertEqual(1, row.ConditionEditor.Variables.Count);
    AssertEqual("B", row.ConditionEditor.Variables[0].Name);
    AssertEqual("custom_signals_1.temperature_filtered", row.ConditionEditor.Variables[0].SourcePath);
    AssertEqual("Condition: {B} >= 25", row.ConditionSummary);
    AssertEqual("Then: 1", row.ThenSummary);
    AssertEqual("Else: 0", row.ElseSummary);
}

static void WorkflowExecutorRunsStepsInOrder()
{
    var visited = new List<string>();
    var definition = new FunctionDefinition
    {
        Name = "startup_sequence",
        Steps =
        [
            new FunctionLogStepDefinition
            {
                TargetLog = "Logs.process",
                Level = MonitorLogLevel.Info,
                Text = "Start"
            },
            new FunctionSetValueStepDefinition
            {
                Target = "studio.main.pump.enable",
                Value = "true"
            },
            new FunctionIfThenElseStepDefinition
            {
                Condition = "temperature > 20",
                Then =
                [
                    new FunctionDelayStepDefinition
                    {
                        Milliseconds = 1
                    },
                    new FunctionLogStepDefinition
                    {
                        TargetLog = "Logs.audit",
                        Text = "Then"
                    }
                ]
            }
        ]
    };

    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                ConditionVariables = new Dictionary<string, object?>
                {
                    ["temperature"] = 21
                },
                EvaluateConditionAsync = (condition, variables, _) =>
                {
                    visited.Add($"condition:{condition}:{variables["temperature"]}");
                    return ValueTask.FromResult(true);
                },
                WriteLogAsync = (step, _) =>
                {
                    visited.Add($"log:{step.TargetLog}:{step.Text}");
                    return ValueTask.CompletedTask;
                },
                SetValueAsync = (step, _) =>
                {
                    visited.Add($"set:{step.Target}:{step.Value}");
                    return ValueTask.CompletedTask;
                }
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Done, result.State);
    AssertEqual(4, visited.Count);
    AssertEqual("log:Logs.process:Start", visited[0]);
    AssertEqual("set:studio.main.pump.enable:true", visited[1]);
    AssertEqual("condition:temperature > 20:21", visited[2]);
    AssertEqual("log:Logs.audit:Then", visited[3]);
}

static void WorkflowExecutorReportsCancellationStateTransitions()
{
    var states = new List<FunctionState>();
    using var cts = new CancellationTokenSource();
    var definition = new FunctionDefinition
    {
        Name = "cancel_me",
        Steps =
        [
            new FunctionDelayStepDefinition
            {
                Milliseconds = 200
            }
        ]
    };

    var executionTask = FunctionExecutor.ExecuteAsync(
        definition,
        new FunctionExecutionEnvironment
        {
            StatusChanged = status => states.Add(status.State)
        },
        cts.Token);

    cts.CancelAfter(20);
    var result = executionTask.GetAwaiter().GetResult();

    AssertEqual(FunctionState.Canceled, result.State);
    AssertTrue(states.Contains(FunctionState.Running));
    AssertTrue(states.Contains(FunctionState.Stopping));
    AssertTrue(states.Contains(FunctionState.Canceled));
}

static void WorkflowExecutorFailsMissingExplicitLogTarget()
{
    var result = FunctionExecutor.ExecuteAsync(
            new FunctionDefinition
            {
                Name = "invalid_log",
                Steps =
                [
                    new FunctionLogStepDefinition
                    {
                        TargetLog = string.Empty,
                        Text = "Missing target"
                    }
                ]
            },
            new FunctionExecutionEnvironment
            {
                WriteLogAsync = (_, _) => ValueTask.CompletedTask
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Failed, result.State);
    AssertTrue(result.ErrorMessage.Contains("explicit target log", StringComparison.Ordinal));
}

static void WorkflowCodecParsesSetValueValueFrom()
{
    var raw = """
name: read_source
steps:
  - type: SetValue
    target: pump.enable
    valueFrom: sensor.status
""";

    AssertTrue(FunctionDefinitionCodec.TryParse(raw, "read_source.yaml", out var definition, out var validation));
    AssertTrue(validation.IsValid);
    AssertTrue(definition is not null);

    var step = (FunctionSetValueStepDefinition)definition!.Steps.Single();
    AssertEqual("pump.enable", step.Target);
    AssertEqual(string.Empty, step.Value);
    AssertEqual("sensor.status", step.ValueFrom);
}

static void WorkflowCodecSerializesSetValueValueFrom()
{
    var definition = new FunctionDefinition
    {
        Name = "read_source",
        Steps =
        [
            new FunctionSetValueStepDefinition
            {
                Target = "pump.enable",
                ValueFrom = "sensor.status"
            }
        ]
    };

    var serialized = FunctionDefinitionCodec.Serialize(definition);

    AssertTrue(serialized.Contains("valueFrom: sensor.status", StringComparison.Ordinal));
    AssertFalse(serialized.Contains("value:", StringComparison.Ordinal));

    AssertTrue(FunctionDefinitionCodec.TryParse(serialized, "read_source.yaml", out var roundtripped, out var validation));
    AssertTrue(validation.IsValid);
    var roundtrippedStep = (FunctionSetValueStepDefinition)roundtripped!.Steps.Single();
    AssertEqual("sensor.status", roundtrippedStep.ValueFrom);
    AssertEqual(string.Empty, roundtrippedStep.Value);
}

static void WorkflowCodecSerializesStructuredSetValueValue()
{
    var structuredValue = SetValueOperationCodec.Serialize(new SetValueOperation
    {
        Kind = SetValueOperationKind.IncrementBy,
        LiteralValue = "2.5",
        IsLegacyLiteral = false
    });
    var definition = new FunctionDefinition
    {
        Name = "increment_source",
        Steps =
        [
            new FunctionSetValueStepDefinition
            {
                Target = "pump.speed",
                Value = structuredValue
            }
        ]
    };

    var serialized = FunctionDefinitionCodec.Serialize(definition);

    AssertTrue(serialized.Contains("value: sv1:", StringComparison.Ordinal));
    AssertFalse(serialized.Contains("valueFrom:", StringComparison.Ordinal));
    AssertTrue(FunctionDefinitionCodec.TryParse(serialized, "increment_source.yaml", out var roundtripped, out var validation));
    AssertTrue(validation.IsValid);
    var roundtrippedStep = (FunctionSetValueStepDefinition)roundtripped!.Steps.Single();
    AssertEqual(structuredValue, roundtrippedStep.Value);
    AssertEqual(string.Empty, roundtrippedStep.ValueFrom);
}

static void WorkflowEditorRowRoundtripsSetValueValueFrom()
{
    var original = new FunctionSetValueStepDefinition
    {
        Target = "pump.enable",
        ValueFrom = "sensor.status"
    };

    var row = FunctionStepEditorRow.FromStep(original);
    AssertEqual("pump.enable", row.Target);
    AssertEqual(string.Empty, row.Value);
    AssertEqual("sensor.status", row.ValueFrom);
    AssertTrue(row.Summary.Contains("from", StringComparison.OrdinalIgnoreCase));

    AssertTrue(FunctionEditorDefinitionConverter.TryBuildDefinition("read_source", [row], out var definition, out var errorMessage));
    AssertTrue(string.IsNullOrEmpty(errorMessage));
    var built = (FunctionSetValueStepDefinition)definition!.Steps.Single();
    AssertEqual("pump.enable", built.Target);
    AssertEqual(string.Empty, built.ValueFrom);
    AssertTrue(SetValueOperationCodec.IsStructuredArgument(built.Value));

    var parsed = SetValueOperationCodec.Parse(built.Value);
    AssertTrue(parsed.IsValid);
    AssertEqual(SetValueOperationKind.SetFromItem, parsed.Operation.Kind);
    AssertEqual("sensor.status", parsed.Operation.SourcePath);
}

static void WorkflowEditorRowMapsLegacyValueFromToStructuredSetFromItem()
{
    var original = new FunctionSetValueStepDefinition
    {
        Target = "pump.enable",
        ValueFrom = "sensor.status"
    };

    var row = FunctionStepEditorRow.FromStep(original);
    row.SetValueTargetKind = SetValueTargetKind.Boolean;

    AssertEqual(SetValueOperationKind.SetFromItem, row.SelectedSetValueOperation?.Kind);
    AssertEqual("sensor.status", row.SetValueSourcePath);
    AssertTrue(row.SetValueSummary.Contains("sensor.status", StringComparison.Ordinal));
    AssertFalse(row.HasSetValueValidationError);
}

static void WorkflowExecutorSetValueResolvesValueFrom()
{
    var writtenTarget = string.Empty;
    var writtenValue = string.Empty;

    var definition = new FunctionDefinition
    {
        Name = "copy_value",
        Steps =
        [
            new FunctionSetValueStepDefinition
            {
                Target = "pump.enable",
                ValueFrom = "sensor.status"
            }
        ]
    };

    var mockSourceValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sensor.status"] = "true"
    };

    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                SetValueAsync = (step, _) =>
                {
                    string resolved;
                    if (!string.IsNullOrWhiteSpace(step.ValueFrom))
                    {
                        if (!mockSourceValues.TryGetValue(step.ValueFrom, out var sourceValue))
                        {
                            throw new InvalidOperationException($"Value source '{step.ValueFrom}' was not found.");
                        }

                        resolved = sourceValue;
                    }
                    else
                    {
                        resolved = step.Value;
                    }

                    writtenTarget = step.Target;
                    writtenValue = resolved;
                    return ValueTask.CompletedTask;
                }
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Done, result.State);
    AssertEqual("pump.enable", writtenTarget);
    AssertEqual("true", writtenValue);
}

static void WorkflowExecutorSetValueFailsUnresolvedValueFrom()
{
    var definition = new FunctionDefinition
    {
        Name = "copy_missing",
        Steps =
        [
            new FunctionSetValueStepDefinition
            {
                Target = "pump.enable",
                ValueFrom = "sensor.missing"
            }
        ]
    };

    var result = FunctionExecutor.ExecuteAsync(
            definition,
            new FunctionExecutionEnvironment
            {
                SetValueAsync = (step, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(step.ValueFrom))
                    {
                        throw new InvalidOperationException($"Value source '{step.ValueFrom}' was not found.");
                    }

                    return ValueTask.CompletedTask;
                }
            })
        .GetAwaiter()
        .GetResult();

    AssertEqual(FunctionState.Failed, result.State);
    AssertTrue(result.ErrorMessage.Contains("sensor.missing", StringComparison.Ordinal));
}

static void WorkflowExecutorSetValueExecutesStructuredIncrement()
{
    const string targetPath = "studio.main.pump.speed";
    var targetItem = ItemExtension.CreateWithPath(targetPath, 10d);
    targetItem.Properties["write"].Value = 10d;
    targetItem.Properties["read"].Value = 10d;
    targetItem.Properties["type"].Value = "double";
    targetItem.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, targetItem);

    try
    {
        var owner = new FolderItemModel
        {
            Kind = ControlKind.Button
        };

        var targetProperty = typeof(FolderItemModel).GetProperty("Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (targetProperty is null)
        {
            throw new InvalidOperationException("Target property was not found.");
        }

        targetProperty.SetValue(owner, targetItem);

        var method = typeof(FolderItemModel).GetMethod(
            "ExecuteRunFunctionSetValueAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(FunctionSetValueStepDefinition), typeof(CancellationToken)],
            null);
        if (method is null)
        {
            throw new InvalidOperationException("ExecuteRunFunctionSetValueAsync was not found.");
        }

        var step = new FunctionSetValueStepDefinition
        {
            Target = targetPath,
            Value = SetValueOperationCodec.Serialize(new SetValueOperation
            {
                Kind = SetValueOperationKind.IncrementBy,
                LiteralValue = "2.5",
                IsLegacyLiteral = false
            })
        };

        var task = (ValueTask)method.Invoke(owner, [step, CancellationToken.None])!;
        task.AsTask().GetAwaiter().GetResult();

        AssertEqual(12.5d, targetItem.Properties["write"].Value);
    }
    finally
    {
        HostRegistries.Data.Remove(targetPath);
    }
}

static void CustomSignalEditorDefaultsToSnakeCaseName()
{
    var ownerItem = new FolderItemModel
    {
        CustomSignalDefinitions = CustomSignalDefinitionCodec.SerializeDefinitions(
        [
            new CustomSignalDefinition { Name = "signal_1" },
            new CustomSignalDefinition { Name = "signal_2" }
        ])
    };

    var viewModel = new CustomSignalEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null);

    AssertEqual("signal_3", viewModel.Name);
}

static void CustomSignalEditorRejectsUppercaseName()
{
    var viewModel = new CustomSignalEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null)
    {
        Name = "CustomSignal1"
    };

    AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
    AssertTrue(errorMessage.Contains("snake_case", StringComparison.Ordinal));
}

static void CustomSignalManualTriggerPathUsesLowercaseSuffix()
{
    var method = typeof(CustomSignalsControl).GetMethod("BuildManualTriggerPath", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(string)], null);
    if (method is null)
    {
        throw new InvalidOperationException("BuildManualTriggerPath was not found.");
    }

    AssertEqual("studio.default_layout.signals.custom.signal_1.trigger", method.Invoke(null, ["studio.default_layout.signals.custom.signal_1"]));
}

static void CustomSignalPublishSnapshotAddsTypeMetadata()
{
    var ownerItem = CreateCustomSignalOwnerItem();
    var definition = new CustomSignalDefinition
    {
        Name = "ready_text",
        DataType = CustomSignalDataType.Text,
        Mode = CustomSignalMode.Input
    };
    var registryPath = InvokeCustomSignalsStaticMethod<string>("BuildRegistryPath", ownerItem, definition);
    HostRegistries.Data.Remove(registryPath);

    try
    {
        var control = (CustomSignalsControl)RuntimeHelpers.GetUninitializedObject(typeof(CustomSignalsControl));
        var method = typeof(CustomSignalsControl).GetMethod(
            "PublishSignalSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(FolderItemModel), typeof(CustomSignalDefinition), typeof(string), typeof(object)],
            modifiers: null);
        if (method is null)
        {
            throw new InvalidOperationException("PublishSignalSnapshot was not found.");
        }

        method.Invoke(control, [ownerItem, definition, registryPath, "online"]);

        AssertTrue(HostRegistries.Data.TryResolve(registryPath, out var published));
        AssertEqual("string", published?.Properties["type"].Value);
    }
    finally
    {
        HostRegistries.Data.Remove(registryPath);
    }
}

static void CustomSignalManualTriggerPublishesBoolTypeMetadata()
{
    var ownerItem = CreateCustomSignalOwnerItem();
    var definition = new CustomSignalDefinition
    {
        Name = "refresh_value",
        Mode = CustomSignalMode.Computed,
        Trigger = CustomSignalComputationTrigger.Manual
    };
    var registryPath = InvokeCustomSignalsStaticMethod<string>("BuildRegistryPath", ownerItem, definition);
    var triggerPath = InvokeCustomSignalsStaticMethod<string>("BuildManualTriggerPath", ownerItem, definition);
    HostRegistries.Data.Remove(triggerPath);
    HostRegistries.Data.Remove(registryPath);

    try
    {
        var control = (CustomSignalsControl)RuntimeHelpers.GetUninitializedObject(typeof(CustomSignalsControl));
        var method = typeof(CustomSignalsControl).GetMethod(
            "PublishManualTriggerSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(FolderItemModel), typeof(CustomSignalDefinition), typeof(string)],
            modifiers: null);
        if (method is null)
        {
            throw new InvalidOperationException("PublishManualTriggerSnapshot was not found.");
        }

        method.Invoke(control, [ownerItem, definition, registryPath]);

        AssertTrue(HostRegistries.Data.TryResolve(triggerPath, out var published));
        AssertEqual("bool", published?.Properties["type"].Value);
    }
    finally
    {
        HostRegistries.Data.Remove(triggerPath);
        HostRegistries.Data.Remove(registryPath);
    }
}

static FolderItemModel CreateCustomSignalOwnerItem()
{
    var ownerItem = new FolderItemModel
    {
        Name = "custom_signals"
    };
    var property = typeof(FolderItemModel).GetProperty("FolderName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    var setter = property?.GetSetMethod(nonPublic: true);
    if (setter is null)
    {
        throw new InvalidOperationException("FolderName setter was not found.");
    }

    setter.Invoke(ownerItem, ["default_layout"]);
    return ownerItem;
}

static T InvokeCustomSignalsStaticMethod<T>(string methodName, params object?[] arguments)
{
    var parameterTypes = arguments.Select(static argument => argument?.GetType() ?? typeof(object)).ToArray();
    var method = typeof(CustomSignalsControl).GetMethod(
        methodName,
        BindingFlags.Static | BindingFlags.NonPublic,
        binder: null,
        types: parameterTypes,
        modifiers: null);
    if (method is null)
    {
        throw new InvalidOperationException($"{methodName} was not found.");
    }

    return (T)method.Invoke(null, arguments)!;
}

static CustomSignalsControl CreateCustomSignalsControlWithSignals(FolderItemModel ownerItem, IEnumerable<CustomSignalRow> rows)
{
    var control = (CustomSignalsControl)RuntimeHelpers.GetUninitializedObject(typeof(CustomSignalsControl));

    var signalsBackingField = typeof(CustomSignalsControl)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .FirstOrDefault(static field => field.Name.Contains("Signals") && field.FieldType == typeof(ObservableCollection<CustomSignalRow>));
    if (signalsBackingField is null)
    {
        throw new InvalidOperationException("Signals backing field was not found.");
    }

    var collection = new ObservableCollection<CustomSignalRow>(rows);
    signalsBackingField.SetValue(control, collection);

    var observedItemField = typeof(CustomSignalsControl)
        .GetField("_observedItem", BindingFlags.Instance | BindingFlags.NonPublic);
    if (observedItemField is null)
    {
        throw new InvalidOperationException("_observedItem field was not found.");
    }

    observedItemField.SetValue(control, ownerItem);
    return control;
}

static bool InvokeIsRelevantSourceChange(CustomSignalsControl control, string registryPath)
{
    var method = typeof(CustomSignalsControl).GetMethod(
        "IsRelevantSourceChange",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(string)],
        modifiers: null);
    if (method is null)
    {
        throw new InvalidOperationException("IsRelevantSourceChange was not found.");
    }

    return (bool)method.Invoke(control, [registryPath])!;
}

static void CustomSignalSourceChangeFilterAcceptsMatchingSourcePath()
{
    var ownerItem = CreateCustomSignalOwnerItem();
    var definition = new CustomSignalDefinition
    {
        Name = "demo_computed",
        Mode = CustomSignalMode.Computed,
        Trigger = CustomSignalComputationTrigger.OnSourceChange,
        SourcePath = "studio.default_layout.signals.custom.demo_custom_bool"
    };
    var registryPath = InvokeCustomSignalsStaticMethod<string>("BuildRegistryPath", ownerItem, definition);
    var row = new CustomSignalRow(ownerItem, definition, registryPath);
    var control = CreateCustomSignalsControlWithSignals(ownerItem, [row]);

    AssertTrue(InvokeIsRelevantSourceChange(control, "studio.default_layout.signals.custom.demo_custom_bool"));
}

static void CustomSignalSourceChangeFilterRejectsUnrelatedPath()
{
    var ownerItem = CreateCustomSignalOwnerItem();
    var definition = new CustomSignalDefinition
    {
        Name = "demo_computed",
        Mode = CustomSignalMode.Computed,
        Trigger = CustomSignalComputationTrigger.OnSourceChange,
        SourcePath = "studio.default_layout.signals.custom.demo_custom_bool"
    };
    var registryPath = InvokeCustomSignalsStaticMethod<string>("BuildRegistryPath", ownerItem, definition);
    var row = new CustomSignalRow(ownerItem, definition, registryPath);
    var control = CreateCustomSignalsControlWithSignals(ownerItem, [row]);

    AssertTrue(!InvokeIsRelevantSourceChange(control, "udl_project.demo_module.some_unrelated_value"));
}

static void CustomSignalEnumerateSourcePathsYieldsAllConfiguredSources()
{
    var definition = new CustomSignalDefinition
    {
        Name = "multi_source",
        Mode = CustomSignalMode.Computed,
        Trigger = CustomSignalComputationTrigger.OnSourceChange,
        SourcePath = "studio.default_layout.signals.custom.a",
        SourcePath2 = "studio.default_layout.signals.custom.b",
        SourcePath3 = "studio.default_layout.signals.custom.c",
        Variables =
        [
            new CustomSignalVariableDefinition { Name = "x", SourcePath = "studio.default_layout.signals.custom.d" }
        ]
    };

    var method = typeof(CustomSignalsControl).GetMethod(
        "EnumerateSourcePaths",
        BindingFlags.Static | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(CustomSignalDefinition)],
        modifiers: null);
    if (method is null)
    {
        throw new InvalidOperationException("EnumerateSourcePaths was not found.");
    }

    var result = ((IEnumerable<string>)method.Invoke(null, [definition])!).ToArray();

    AssertEqual(4, result.Length);
    AssertTrue(result.Contains("studio.default_layout.signals.custom.d", StringComparer.OrdinalIgnoreCase));
    AssertTrue(result.Contains("studio.default_layout.signals.custom.a", StringComparer.OrdinalIgnoreCase));
    AssertTrue(result.Contains("studio.default_layout.signals.custom.b", StringComparer.OrdinalIgnoreCase));
    AssertTrue(result.Contains("studio.default_layout.signals.custom.c", StringComparer.OrdinalIgnoreCase));
}

static void ScopedRegistryFilterMatchesDescendantPathWithoutSiblingBleed()
{
    var prefixes = ScopedRegistryEventFilter.NormalizePrefixes([
        "studio.default_layout.controller.demo_controller"
    ]);

    AssertTrue(ScopedRegistryEventFilter.TryMatchPrefix(
        key: "studio.default_layout.controller.demo_controller.state",
        prefixes: prefixes,
        matchedPrefix: out var matchedPrefix));
    AssertEqual("studio.default_layout.controller.demo_controller", matchedPrefix);

    AssertTrue(!ScopedRegistryEventFilter.TryMatchPrefix(
        key: "studio.default_layout.controller.demo_controller_2.state",
        prefixes: prefixes,
        matchedPrefix: out _));
}

static void ScopedRegistryFilterCanRejectAncestorPaths()
{
    var prefixes = ScopedRegistryEventFilter.NormalizePrefixes([
        "studio.default_layout.monitor.rule_a"
    ]);

    AssertTrue(ScopedRegistryEventFilter.TryMatchPrefix(
        key: "studio.default_layout.monitor.rule_a.active",
        prefixes: prefixes,
        matchedPrefix: out var matchedPrefix,
        includeAncestorMatches: false));
    AssertEqual("studio.default_layout.monitor.rule_a", matchedPrefix);

    AssertFalse(ScopedRegistryEventFilter.TryMatchPrefix(
        key: "studio.default_layout.monitor",
        prefixes: prefixes,
        matchedPrefix: out _,
        includeAncestorMatches: false));
}

static void ScopedRegistryFilterCollapsesRedundantPrefixes()
{
    var prefixes = ScopedRegistryEventFilter.NormalizePrefixes([
        "studio.default_layout.monitor",
        "studio.default_layout.monitor.rule_a",
        "studio.default_layout.monitor.rule_a.active"
    ]);

    AssertEqual(1, prefixes.Length);
    AssertEqual("studio.default_layout.monitor", prefixes[0]);
}

static void MonitorCodecPreservesMultipleActionsPerTrigger()
{
    var raw = MonitorDefinitionCodec.SerializeDefinitions(
    [
        new MonitorDefinition
        {
            Name = "pressure_low",
            SourcePath = "studio.default_layout.sensors.pressure",
            RefreshRateMs = 500,
            Mode = MonitorRuleMode.Default,
            EventId = 1001,
            EventText = "Pressure below lower limit",
            LogLevel = MonitorLogLevel.Warning,
            Actions =
            [
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnActivated,
                    ActionType = MonitorActionType.WriteLog,
                    TargetLog = "Logs.process"
                },
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnActivated,
                    ActionType = MonitorActionType.WriteLog,
                    TargetLog = "Logs.audit"
                },
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnCleared,
                    ActionType = MonitorActionType.WriteLog,
                    TargetLog = "Logs.process"
                }
            ]
        }
    ]);

    var parsed = MonitorDefinitionCodec.ParseDefinitions(raw);

    AssertEqual(1, parsed.Count);
    AssertEqual(3, parsed[0].Actions.Count);
    AssertEqual(MonitorActionTrigger.OnActivated, parsed[0].Actions[0].Trigger);
    AssertEqual(MonitorActionTrigger.OnActivated, parsed[0].Actions[1].Trigger);
    AssertEqual("logs.process", parsed[0].Actions[0].TargetLog);
    AssertEqual("logs.audit", parsed[0].Actions[1].TargetLog);
    AssertEqual(MonitorActionTrigger.OnCleared, parsed[0].Actions[2].Trigger);
}

static void MonitorEditorAcceptsMultipleActionsPerTrigger()
{
    var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null, targetLogOptions: ["Logs.process", "Logs.audit"])
    {
        SourcePath = "Logs.source"
    };

    viewModel.AddAction(MonitorActionTrigger.OnActivated.ToString(), MonitorActionType.WriteLog.ToString(), "Logs.process");
    viewModel.AddAction(MonitorActionTrigger.OnActivated.ToString(), MonitorActionType.WriteLog.ToString(), "Logs.audit");

    AssertTrue(viewModel.TryBuildDefinition(out var definition, out var errorMessage));
    AssertEqual(string.Empty, errorMessage);
    AssertEqual(2, definition.Actions.Count);
    AssertEqual(MonitorActionTrigger.OnActivated, definition.Actions[0].Trigger);
    AssertEqual(MonitorActionTrigger.OnActivated, definition.Actions[1].Trigger);
    AssertEqual("logs.process", definition.Actions[0].TargetLog);
    AssertEqual("logs.audit", definition.Actions[1].TargetLog);
}

static void MonitorEditorAccepts100MsRefreshRate()
{
    var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null, targetLogOptions: [])
    {
        SourcePath = "Logs.source",
        RefreshRateMsText = "100"
    };

    AssertTrue(viewModel.TryBuildDefinition(out var definition, out var errorMessage));
    AssertEqual(string.Empty, errorMessage);
    AssertEqual(100, definition.RefreshRateMs);
}

static void MonitorEditorRejectsWriteLogActionWithoutTarget()
{
    var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null, targetLogOptions: [])
    {
        SourcePath = "Logs.source"
    };

    viewModel.AddAction(MonitorActionTrigger.OnActivated.ToString(), MonitorActionType.WriteLog.ToString(), string.Empty);

    AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
    AssertTrue(errorMessage.Contains("target log", StringComparison.Ordinal));
}

static void MonitorCodecPreservesActionSpecificFields()
{
    var raw = MonitorDefinitionCodec.SerializeDefinitions(
    [
        new MonitorDefinition
        {
            Name = "monitor_actions",
            SourcePath = "studio.default_layout.sensors.pressure",
            Actions =
            [
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnActivated,
                    ActionType = MonitorActionType.SetValue,
                    TargetPath = "runtime.outputs.setpoint",
                    Argument = "42"
                },
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnCleared,
                    ActionType = MonitorActionType.InvokeFunction,
                    TargetPath = "app_demo:runtime",
                    FunctionName = "reset_alarm",
                    Argument = "{\"value\":false}"
                }
            ]
        }
    ]);

    var parsed = MonitorDefinitionCodec.ParseDefinitions(raw);

    AssertEqual(1, parsed.Count);
    AssertEqual(2, parsed[0].Actions.Count);
    AssertEqual(MonitorActionType.SetValue, parsed[0].Actions[0].ActionType);
    AssertEqual("runtime.outputs.setpoint", parsed[0].Actions[0].TargetPath);
    AssertEqual("42", parsed[0].Actions[0].Argument);
    AssertEqual(MonitorActionType.InvokeFunction, parsed[0].Actions[1].ActionType);
    AssertEqual("app_demo:runtime", parsed[0].Actions[1].TargetPath);
    AssertEqual("reset_alarm", parsed[0].Actions[1].FunctionName);
    AssertEqual("{\"value\":false}", parsed[0].Actions[1].Argument);
}

static void MonitorYamlControlDefinitionWritesMonitorDefinitions()
{
    var method = typeof(MainWindowViewModel).GetMethod("BuildYamlControlDefinition", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildYamlControlDefinition was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.Monitor,
        Name = "monitor_1",
        MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(
        [
            new MonitorDefinition
            {
                Name = "pressure_low",
                SourcePath = "studio.default_layout.sensors.pressure",
                LowerLimit = "2.5",
                Actions =
                [
                    new MonitorActionDefinition
                    {
                        Trigger = MonitorActionTrigger.OnActivated,
                        ActionType = MonitorActionType.WriteLog,
                        TargetLog = "Logs.process"
                    }
                ]
            }
        ])
    };

    var node = (JsonObject?)method.Invoke(null, [item]);
    var properties = node?["Properties"] as JsonObject;
    var monitorDefinitions = properties?["MonitorDefinitions"] as JsonArray;

    AssertTrue(monitorDefinitions is not null);
    AssertEqual(1, monitorDefinitions!.Count);
    AssertEqual("pressure_low", monitorDefinitions[0]?["Name"]?.GetValue<string>());
}

static void MonitorFileCodecRoundtripsCentralYaml()
{
    var rootDirectory = CreateTempDirectory();
    var codec = new MonitorDefinitionFileCodec();

    var filePath = codec.SaveDefinitions(
        folderDirectory: rootDirectory,
        folderName: "main",
        definitions:
        [
            new MonitorDefinition
            {
                Name = "pressure_low",
                SourcePath = "studio.main.sensors.pressure",
                RefreshRateMs = 500,
                LowerLimit = "2.5",
                EventId = 1001,
                EventText = "Pressure below lower limit",
                LogLevel = MonitorLogLevel.Warning,
                Actions =
                [
                    new MonitorActionDefinition
                    {
                        Trigger = MonitorActionTrigger.OnActivated,
                        ActionType = MonitorActionType.WriteLog,
                        TargetLog = "Logs.process"
                    }
                ]
            }
        ]);

    AssertTrue(File.Exists(filePath));
    AssertEqual(Path.Combine(rootDirectory, "Monitoring", "Monitor.yaml"), filePath);

    var raw = File.ReadAllText(filePath);
    AssertTrue(raw.Contains("rules:", StringComparison.OrdinalIgnoreCase));
    AssertTrue(raw.Contains("name: pressure_low", StringComparison.OrdinalIgnoreCase));

    var definitions = codec.LoadDefinitions(rootDirectory, "main");
    AssertEqual(1, definitions.Count);
    AssertEqual("pressure_low", definitions[0].Name);
    AssertEqual("studio.main.sensors.pressure", definitions[0].SourcePath);
    AssertEqual(500, definitions[0].RefreshRateMs);
    AssertEqual("2.5", definitions[0].LowerLimit);
    AssertEqual(1001, definitions[0].EventId);
    AssertEqual(1, definitions[0].Actions.Count);
    AssertEqual(MonitorActionType.WriteLog, definitions[0].Actions[0].ActionType);
    AssertEqual("logs.process", definitions[0].Actions[0].TargetLog);
}

static void MonitorFileCodecLoadsCustomRuleWithoutSource()
{
    var rootDirectory = CreateTempDirectory();
    var monitoringDirectory = MonitorDefinitionFileCodec.GetMonitoringDirectory(rootDirectory);
    Directory.CreateDirectory(monitoringDirectory);
    File.WriteAllText(
        MonitorDefinitionFileCodec.GetMonitorFilePath(rootDirectory),
        """
        rules:
        - name: monitor_rule_1
          sourcePath: ''
          refreshRateMs: 1000
          mode: Custom
          lowerLimit: ''
          upperLimit: ''
          inhibitMs: 100
          customFormula: '{A} > 10'
          customVariables:
          - name: A
            sourcePath: enhanced_signals.enhanced_signal_1.read
          condition: ''
          eventId: 1
          eventText: '>10'
          actions: []
          targetLog: ''
          logLevel: Info
        """);

    var definitions = new MonitorDefinitionFileCodec().LoadDefinitions(rootDirectory, "main");

    AssertEqual(1, definitions.Count);
    AssertEqual("monitor_rule_1", definitions[0].Name);
    AssertEqual(MonitorRuleMode.Custom, definitions[0].Mode);
    AssertEqual(string.Empty, definitions[0].SourcePath);
    AssertEqual("{A} > 10", definitions[0].CustomFormula);
    AssertEqual(1, definitions[0].CustomVariables.Count);
    AssertEqual("A", definitions[0].CustomVariables[0].Name);
    AssertEqual("enhanced_signals.enhanced_signal_1.read", definitions[0].CustomVariables[0].SourcePath);
    AssertEqual(1, definitions[0].EventId);
    AssertEqual(MonitorLogLevel.Info, definitions[0].LogLevel);
}

static void MonitorRegistryPrefersCentralFileDefinitions()
{
    var rootDirectory = CreateTempDirectory();
    var fileCodec = new MonitorDefinitionFileCodec();
    fileCodec.SaveDefinitions(
        folderDirectory: rootDirectory,
        folderName: "main",
        definitions:
        [
            new MonitorDefinition
            {
                Name = "pressure_low",
                SourcePath = "studio.main.sensors.pressure",
                EventId = 2001
            },
            new MonitorDefinition
            {
                Name = "temperature_high",
                SourcePath = "studio.main.sensors.temperature",
                EventId = 2002
            }
        ]);

    var legacyMonitorItem = new FolderItemModel
    {
        Kind = ControlKind.Monitor,
        Name = "monitor_1",
        MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(
        [
            new MonitorDefinition
            {
                Name = "pressure_low",
                SourcePath = "studio.main.legacy.pressure",
                EventId = 1001
            },
            new MonitorDefinition
            {
                Name = "legacy_only",
                SourcePath = "studio.main.legacy.other",
                EventId = 1002
            }
        ])
    };

    var entries = MonitorRegistry.EnumerateEntries(rootDirectory, "main", [legacyMonitorItem]);

    AssertEqual(2, entries.Count);
    AssertEqual(MonitorRegistrySource.CentralFile, entries[0].Source);
    AssertEqual(MonitorRegistrySource.CentralFile, entries[1].Source);
    AssertTrue(entries.Any(entry => string.Equals(entry.Name, "pressure_low", StringComparison.OrdinalIgnoreCase)));
    AssertTrue(entries.Any(entry => string.Equals(entry.Name, "temperature_high", StringComparison.OrdinalIgnoreCase)));
    AssertFalse(entries.Any(entry => string.Equals(entry.Name, "legacy_only", StringComparison.OrdinalIgnoreCase)));
    AssertEqual("studio.main.monitor.pressure_low", MonitorRegistry.BuildRulePath("main", "pressure_low"));
    AssertEqual("studio.main.monitor", MonitorRegistry.BuildAggregatePath("main"));
}

static void MonitorRuntimeManagerPublishesWithoutWidget()
{
    var definition = new MonitorDefinition
    {
        Name = "pressure_low",
        SourcePath = "studio.main.sensors.pressure",
        RefreshRateMs = 250,
        EventId = 1001,
        EventText = "Pressure below lower limit"
    };

    try
    {
        var rows = MonitorRuntimeManager.SyncDefinitions("main", [definition], forceRecreate: true);

        AssertEqual(1, rows.Count);
        AssertTrue(WaitForRegistryPath(MonitorRegistry.BuildRulePath("main", "pressure_low"), out var ruleItem));
        AssertTrue(ruleItem is not null);
        AssertTrue(HostRegistries.Data.TryGet(MonitorRegistry.BuildAggregatePath("main"), out var aggregateItem));
        AssertTrue(aggregateItem is not null);
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder("main");
    }
}

static void MonitorRuntimeManagerUpdatesChangedDefinitionWithSameName()
{
    var sourcePath = "studio.editor_tests.monitor.signature.source";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 1d));

    try
    {
        var firstDefinition = new MonitorDefinition
        {
            Name = "pressure_low",
            SourcePath = sourcePath,
            RefreshRateMs = 250,
            LowerLimit = "5",
            EventId = 1001,
            EventText = "Initial"
        };

        var initialRuntimes = MonitorRuntimeManager.SyncDefinitions("main", [firstDefinition], forceRecreate: true);
        AssertEqual(1, initialRuntimes.Count);
        var firstRuntime = initialRuntimes[0];

        var updatedDefinition = firstDefinition.Clone();
        updatedDefinition.LowerLimit = "0.5";
        updatedDefinition.EventText = "Updated";

        var updatedRuntimes = MonitorRuntimeManager.SyncDefinitions("main", [updatedDefinition], forceRecreate: false);
        AssertEqual(1, updatedRuntimes.Count);
        AssertFalse(ReferenceEquals(firstRuntime, updatedRuntimes[0]));
        AssertEqual("Updated", updatedRuntimes[0].Definition.EventText);
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder("main");
        HostRegistries.Data.Remove(sourcePath);
    }
}

static void MainWindowMonitorSyncKeepsBackgroundRuntimeWithMonitorBrowser()
{
    var sourcePath = "studio.editor_tests.monitor.background.source";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 1d));

    var viewModel = new MainWindowViewModel();
    var page = viewModel.SelectedFolder;
    var monitorItem = new FolderItemModel
    {
        Kind = ControlKind.Monitor,
        Name = "monitor_browser",
        MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(
        [
            new MonitorDefinition
            {
                Name = "pressure_low",
                SourcePath = sourcePath,
                LowerLimit = "5",
                RefreshRateMs = 250,
                EventId = 1001,
                EventText = "Pressure below lower limit"
            }
        ])
    };
    monitorItem.SetHierarchy(page.Name, parentItem: null);
    page.Items.Add(monitorItem);

    try
    {
        var method = typeof(MainWindowViewModel).GetMethod("SyncMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
        {
            throw new InvalidOperationException("SyncMonitors was not found.");
        }

        method.Invoke(viewModel, [page, false]);

        AssertTrue(WaitForRegistryPath(MonitorRegistry.BuildRulePath(page.Name, "pressure_low"), out var ruleItem));
        AssertTrue(ruleItem is not null);
        AssertTrue(HostRegistries.Data.TryGet(MonitorRegistry.BuildAggregatePath(page.Name), out var aggregateItem));
        AssertTrue(aggregateItem is not null);
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder(page.Name);
        HostRegistries.Data.Remove(sourcePath);
        page.Items.Clear();
    }
}

static void MainWindowMonitorSyncLoadsCentralDefinitions()
{
    var folderDirectory = CreateTemporaryDirectory();
    var folderYamlPath = Path.Combine(folderDirectory, "Folder.yaml");
    File.WriteAllText(folderYamlPath, "Caption: 'main'");

    var page = new FolderModel
    {
        Name = "main",
        DisplayText = "main",
        UiFilePath = folderYamlPath
    };

    var definition = new MonitorDefinition
    {
        Name = "pressure_low",
        SourcePath = "studio.main.sensors.pressure",
        LowerLimit = "5",
        RefreshRateMs = 250,
        EventId = 1001,
        EventText = "Pressure below lower limit"
    };
    new MonitorDefinitionFileCodec().SaveDefinitions(folderDirectory, page.Name, [definition]);

    var viewModel = new MainWindowViewModel();
    viewModel.Folders.Clear();
    viewModel.Folders.Add(page);
    viewModel.SelectedFolder = page;

    try
    {
        InvokeMainWindowMonitorSync(viewModel, page, forceRecreate: false);

        AssertTrue(WaitForRegistryPath(MonitorRegistry.BuildRulePath(page.Name, "pressure_low"), out var ruleItem));
        AssertTrue(ruleItem is not null);
        AssertTrue(HostRegistries.Data.TryGet(MonitorRegistry.BuildAggregatePath(page.Name), out var aggregateItem));
        AssertTrue(aggregateItem is not null);
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder(page.Name);
    }
}

static void SignalHistoryRuntimeSkipsZeroHistory()
{
    try
    {
        SignalHistoryRuntimeManager.SyncDefinitions(
            "main",
            [
                SignalHistoryRuntimeManager.SignalHistorySourceDefinition.Create(
                    targetPath: "studio.main.signals.temperature",
                    pageName: "main",
                    displayName: "temperature",
                    historySeconds: 0,
                    refreshRateMs: 100)
            ]);

        AssertFalse(SignalHistoryRuntimeManager.TryGetRuntime("main", out _));
    }
    finally
    {
        SignalHistoryRuntimeManager.ReleaseFolder("main");
    }
}

static void SignalHistoryRuntimeRecordsConfiguredSource()
{
    var sourcePath = "studio.editor_tests.chart.history_signal";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 10d));

    try
    {
        SignalHistoryRuntimeManager.SyncDefinitions(
            "main",
            [
                SignalHistoryRuntimeManager.SignalHistorySourceDefinition.Create(
                    targetPath: sourcePath,
                    pageName: "main",
                    displayName: "HistorySignal",
                    historySeconds: 30,
                    refreshRateMs: 50)
            ]);

        AssertTrue(SignalHistoryRuntimeManager.TryGetRuntime("main", out var runtime));
        AssertTrue(runtime is not null);
        AssertTrue(SpinWait.SpinUntil(
            () => runtime!.GetPoints(sourcePath, "main", DateTime.Now.AddSeconds(-30), DateTime.Now.AddSeconds(1)).Length > 0,
            millisecondsTimeout: 2000));

        HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 21d));
        AssertTrue(SpinWait.SpinUntil(
            () => runtime!.GetPoints(sourcePath, "main", DateTime.Now.AddSeconds(-30), DateTime.Now.AddSeconds(1)).Any(point => Math.Abs(point.Value - 21d) < 0.0001),
            millisecondsTimeout: 2000));

        var snapshot = runtime!.GetPoints(sourcePath, "main", DateTime.Now.AddSeconds(-30), DateTime.Now.AddSeconds(1));
        AssertTrue(snapshot.Length > 0);
        AssertTrue(snapshot.All(point => point.Timestamp >= DateTime.Now.AddSeconds(-31)));
    }
    finally
    {
        SignalHistoryRuntimeManager.ReleaseFolder("main");
        HostRegistries.Data.Remove(sourcePath);
    }
}

static void ChartDataProviderReturnsEmptySeriesWithoutHistory()
{
    using var provider = new ChartDataProvider("main");
    var configuration = new RealtimeChartRuntimeManager.ChartSeriesConfiguration(
        TargetPath: "studio.main.signals.missing_history",
        PageName: "main",
        AxisIndex: 1,
        ConnectStyle: ScottPlot.ConnectStyle.Straight,
        Key: "studio.main.signals.missing_history|Y1|Line",
        DisplayName: "missing_history");

    provider.UpdateSeriesConfigurations([configuration]);
    provider.RequestSnapshotRefresh(viewSeconds: 30, maxRenderPoints: 256);

    AssertTrue(SpinWait.SpinUntil(
        () => provider.GetSnapshot().SeriesSnapshots.Count == 1,
        millisecondsTimeout: 2000));

    var snapshot = provider.GetSnapshot();
    AssertEqual(1, snapshot.SeriesSnapshots.Count);
    AssertEqual(0, snapshot.SeriesSnapshots[0].Values.Length);
    AssertEqual(0, snapshot.SeriesSnapshots[0].Timestamps.Length);
}

static void MonitorBrowserResolvesCentralDefinitions()
{
    var folderDirectory = CreateTemporaryDirectory();
    var folderYamlPath = Path.Combine(folderDirectory, "Folder.yaml");
    File.WriteAllText(folderYamlPath, "Caption: 'main'");

    var viewModel = new MainWindowViewModel();
    var page = new FolderModel
    {
        Name = "main",
        DisplayText = "main",
        UiFilePath = folderYamlPath
    };
    viewModel.Folders.Clear();
    viewModel.Folders.Add(page);
    viewModel.SelectedFolder = page;

    var definition = new MonitorDefinition
    {
        Name = "pressure_low",
        SourcePath = "studio.main.sensors.pressure",
        LowerLimit = "5",
        RefreshRateMs = 250,
        EventId = 1001,
        EventText = "Pressure below lower limit"
    };
    new MonitorDefinitionFileCodec().SaveDefinitions(folderDirectory, page.Name, [definition]);

    var browserItem = new FolderItemModel
    {
        Kind = ControlKind.Monitor,
        Name = "MonitorBrowser"
    };
    browserItem.SetHierarchy(page.Name, parentItem: null);
    browserItem.SetLayoutFilePath(folderYamlPath);

    var resolvedDefinitions = InvokeMonitorControlResolveDefinitions(browserItem, viewModel);

    AssertEqual(1, resolvedDefinitions.Count);
    AssertEqual("pressure_low", resolvedDefinitions[0].Name);
}

static void MonitorBrowserResolvesCentralDefinitionsWithoutViewModel()
{
    var folderDirectory = CreateTemporaryDirectory();
    var folderYamlPath = Path.Combine(folderDirectory, "Folder.yaml");
    File.WriteAllText(folderYamlPath, "Caption: 'main'");

    var definition = new MonitorDefinition
    {
        Name = "pressure_low",
        SourcePath = "studio.main.sensors.pressure",
        LowerLimit = "5",
        RefreshRateMs = 250,
        EventId = 1001,
        EventText = "Pressure below lower limit"
    };
    new MonitorDefinitionFileCodec().SaveDefinitions(folderDirectory, "main", [definition]);

    var browserItem = new FolderItemModel
    {
        Kind = ControlKind.Monitor,
        Name = "MonitorBrowser"
    };
    browserItem.SetHierarchy("main", parentItem: null);
    browserItem.SetLayoutFilePath(folderYamlPath);

    var resolvedDefinitions = InvokeMonitorControlResolveDefinitions(browserItem, viewModel: null);

    AssertEqual(1, resolvedDefinitions.Count);
    AssertEqual("pressure_low", resolvedDefinitions[0].Name);
}

static void InvokeMainWindowMonitorSync(MainWindowViewModel viewModel, FolderModel page, bool forceRecreate)
{
    var method = typeof(MainWindowViewModel).GetMethod("SyncMonitors", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("SyncMonitors was not found.");
    }

    method.Invoke(viewModel, [page, forceRecreate]);
}

static void InvokeMainWindowSetFolders(MainWindowViewModel viewModel, IReadOnlyList<FolderModel> pages)
{
    var method = typeof(MainWindowViewModel).GetMethod("SetFolders", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("SetFolders was not found.");
    }

    method.Invoke(viewModel, [pages]);
}

static IReadOnlyList<MonitorRegistryEntry> InvokeMainWindowGetMonitorRegistryEntries(MainWindowViewModel viewModel, FolderItemModel item)
{
    var method = typeof(MainWindowViewModel).GetMethod("GetMonitorRegistryEntries", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("GetMonitorRegistryEntries was not found.");
    }

    return (IReadOnlyList<MonitorRegistryEntry>)method.Invoke(viewModel, [item])!;
}

static bool WaitForRegistryPath(string path, out ItemModel? item)
{
    ItemModel? capturedItem = null;
    var resolved = SpinWait.SpinUntil(
        () =>
        {
            if (!HostRegistries.Data.TryGet(path, out var currentItem) || currentItem is null)
            {
                return false;
            }

            capturedItem = currentItem;
            return true;
        },
        TimeSpan.FromSeconds(2));

    item = capturedItem;
    return resolved;
}

static IReadOnlyList<MonitorDefinition> InvokeMonitorControlResolveDefinitions(FolderItemModel item, MainWindowViewModel? viewModel)
{
    var method = typeof(MonitorControl).GetMethod(
        "ResolveDefinitions",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(FolderItemModel), typeof(MainWindowViewModel)],
        modifiers: null);
    if (method is null)
    {
        throw new InvalidOperationException("MonitorControl.ResolveDefinitions was not found.");
    }

    return (IReadOnlyList<MonitorDefinition>)method.Invoke(null, [item, viewModel])!;
}

static void MonitorControlDetachKeepsRuntimeAggregate()
{
    var sourcePath = "studio.editor_tests.monitor.detach.source";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, 1d));

    try
    {
        MonitorRuntimeManager.SyncDefinitions(
            "main",
            [
                new MonitorDefinition
                {
                    Name = "pressure_low",
                    SourcePath = sourcePath,
                    LowerLimit = "5",
                    RefreshRateMs = 250,
                    EventId = 1001,
                    EventText = "Pressure below lower limit"
                }
            ],
            forceRecreate: true);

        var aggregatePath = MonitorRegistry.BuildAggregatePath("main");
        AssertTrue(HostRegistries.Data.TryGet(aggregatePath, out var initialAggregate));
        AssertTrue(initialAggregate is not null);

        var item = new FolderItemModel
        {
            Kind = ControlKind.Monitor,
            Name = "monitor_browser"
        };
        item.SetHierarchy("main", parentItem: null);

        var control = (MonitorControl)RuntimeHelpers.GetUninitializedObject(typeof(MonitorControl));
        var rulesBackingField = typeof(MonitorControl)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(static field => field.Name.Contains("Rules") && field.FieldType == typeof(ObservableCollection<MonitorRuleRow>));
        if (rulesBackingField is null)
        {
            throw new InvalidOperationException("Monitor rules backing field was not found.");
        }

        rulesBackingField.SetValue(control, new ObservableCollection<MonitorRuleRow>());

        var displayRulesBackingField = typeof(MonitorControl)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(static field => field.Name.Contains("DisplayRules") && field.FieldType == typeof(ObservableCollection<MonitorRuleRow>));
        if (displayRulesBackingField is null)
        {
            throw new InvalidOperationException("Monitor display rules backing field was not found.");
        }

        displayRulesBackingField.SetValue(control, new ObservableCollection<MonitorRuleRow>());

        var observedItemField = typeof(MonitorControl).GetField("_observedItem", BindingFlags.Instance | BindingFlags.NonPublic);
        if (observedItemField is null)
        {
            throw new InvalidOperationException("_observedItem field was not found.");
        }

        observedItemField.SetValue(control, item);

        var detachMethod = typeof(MonitorControl).GetMethod(
            "OnDetachedFromVisualTree",
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(object), typeof(Avalonia.VisualTreeAttachmentEventArgs)],
            modifiers: null);
        if (detachMethod is null)
        {
            throw new InvalidOperationException("MonitorControl detach helper method was not found.");
        }

        detachMethod.Invoke(control, [null, null]);

        AssertTrue(HostRegistries.Data.TryGet(aggregatePath, out var aggregateAfterDetach));
        AssertTrue(aggregateAfterDetach is not null);
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder("main");
        HostRegistries.Data.Remove(sourcePath);
    }
}

static void MonitorViewSelectionNormalizesIds()
{
    var raw = "Pressure_Low, temperature_high\npressure_low ; custom alarm";
    var parsed = MonitorRegistry.ParseSelectedIds(raw);

    AssertEqual(3, parsed.Count);
    AssertEqual("pressure_low", parsed[0]);
    AssertEqual("temperature_high", parsed[1]);
    AssertEqual("custom_alarm", parsed[2]);

    var array = MonitorRegistry.ToJsonArray(raw);
    AssertEqual(3, array.Count);
    AssertEqual("pressure_low", array[0]?.GetValue<string>());

    var roundtrip = MonitorRegistry.FromJsonNode(array);
    AssertEqual("pressure_low, temperature_high, custom_alarm", roundtrip);
}

static void MonitorViewSelectionRejectsDuplicateIds()
{
    var parsed = MonitorRegistry.ParseSelectedIds("Pressure_Low, pressure_low; PRESSURE_LOW");

    AssertEqual(1, parsed.Count);
    AssertEqual("pressure_low", parsed[0]);

    var serialized = MonitorRegistry.SerializeSelectedIds(["Pressure_Low", "pressure_low", "PRESSURE_LOW"]);
    AssertEqual("pressure_low", serialized);
}

static void MonitorPickerUsesStableRuleLabels()
{
    var entries = new[]
    {
        new MonitorRegistryEntry(
            Name: "monitor_rule_1",
            Definition: new MonitorDefinition
            {
                Name = "monitor_rule_1",
                EventId = 1,
                EventText = "Pressure high",
                LogLevel = MonitorLogLevel.Warning,
                SourcePath = "signals.pressure.read"
            },
            Source: MonitorRegistrySource.CentralFile,
            SourceIdentifier: "Monitoring/Monitor.yaml")
    };

    var option = MonitorRegistry.CreateSelectionOptions(entries).Single();
    AssertEqual("monitor_rule_1", option.Name);
    AssertEqual("1 - Pressure high (monitor_rule_1)", option.Label);
    AssertTrue(option.Description.Contains("LogLevel: Warning", StringComparison.Ordinal));

    var serialized = MonitorRegistry.SerializeSelectionOption(option);
    AssertTrue(MonitorRegistry.TryParseSelectionOption(serialized, out var parsed));
    AssertEqual(option.Name, parsed.Name);
    AssertEqual(option.Label, parsed.Label);
}

static void MonitorViewResolvesOnlySelectedRules()
{
    var entries = new[]
    {
        CreateMonitorRegistryEntry("monitor_rule_1", eventId: 1, eventText: "Pressure high"),
        CreateMonitorRegistryEntry("monitor_rule_2", eventId: 2, eventText: "Temperature high"),
        CreateMonitorRegistryEntry("monitor_rule_3", eventId: 3, eventText: "Flow low")
    };

    var selectedEntries = InvokeMonitorViewSelectedEntryResolution("monitor_rule_3, monitor_rule_1, monitor_rule_3", entries);

    AssertEqual(2, selectedEntries.Count);
    AssertEqual("monitor_rule_3", selectedEntries[0].Name);
    AssertEqual("monitor_rule_1", selectedEntries[1].Name);
    AssertFalse(selectedEntries.Any(static entry => string.Equals(entry.Name, "monitor_rule_2", StringComparison.OrdinalIgnoreCase)));
}

static void MonitorViewRebuildsSelectedRulesAfterProjectRuntimeReady()
{
    var folderDirectory = CreateTemporaryDirectory();
    var folderYamlPath = Path.Combine(folderDirectory, "Folder.yaml");
    File.WriteAllText(folderYamlPath, "Caption: 'main'");

    var monitorViewItem = new FolderItemModel
    {
        Kind = ControlKind.MonitorView,
        Name = "monitor_view_1",
        SelectedMonitorIds = "pressure_low"
    };
    monitorViewItem.SetHierarchy("main", parentItem: null);
    monitorViewItem.SetLayoutFilePath(folderYamlPath);

    var earlySelectedEntries = InvokeMonitorViewSelectedEntryResolution(monitorViewItem.SelectedMonitorIds, []);
    AssertEqual(0, earlySelectedEntries.Count);

    var page = new FolderModel
    {
        Name = "main",
        DisplayText = "main",
        UiFilePath = folderYamlPath
    };
    page.Items.Add(monitorViewItem);

    var definition = new MonitorDefinition
    {
        Name = "pressure_low",
        SourcePath = "studio.main.sensors.pressure",
        LowerLimit = "5",
        RefreshRateMs = 250,
        EventId = 1001,
        EventText = "Pressure below lower limit"
    };
    new MonitorDefinitionFileCodec().SaveDefinitions(folderDirectory, page.Name, [definition]);

    var viewModel = new MainWindowViewModel();
    try
    {
        InvokeMainWindowSetFolders(viewModel, [page]);

        var readySelectedEntries = InvokeMonitorViewSelectedEntryResolution(
            monitorViewItem.SelectedMonitorIds,
            InvokeMainWindowGetMonitorRegistryEntries(viewModel, monitorViewItem));

        AssertTrue(viewModel.ProjectRuntimeGeneration > 0);
        AssertEqual(1, readySelectedEntries.Count);
        AssertEqual("pressure_low", readySelectedEntries[0].Name);
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder(page.Name);
    }
}

static void MonitorViewRuntimeGenerationAdvancesForRepeatedProjectLoad()
{
    var firstDirectory = CreateTemporaryDirectory();
    var secondDirectory = CreateTemporaryDirectory();
    var firstYamlPath = Path.Combine(firstDirectory, "Folder.yaml");
    var secondYamlPath = Path.Combine(secondDirectory, "Folder.yaml");
    File.WriteAllText(firstYamlPath, "Caption: 'main'");
    File.WriteAllText(secondYamlPath, "Caption: 'main'");

    var firstDefinition = new MonitorDefinition
    {
        Name = "pressure_low",
        SourcePath = "studio.main.sensors.pressure",
        LowerLimit = "5",
        RefreshRateMs = 250,
        EventId = 1001,
        EventText = "Pressure below lower limit"
    };
    var secondDefinition = new MonitorDefinition
    {
        Name = "temperature_high",
        SourcePath = "studio.main.sensors.temperature",
        UpperLimit = "80",
        RefreshRateMs = 250,
        EventId = 1002,
        EventText = "Temperature above upper limit"
    };
    new MonitorDefinitionFileCodec().SaveDefinitions(firstDirectory, "main", [firstDefinition]);
    new MonitorDefinitionFileCodec().SaveDefinitions(secondDirectory, "main", [secondDefinition]);

    var viewModel = new MainWindowViewModel();
    try
    {
        var firstItem = CreateMonitorViewTestItem("main", firstYamlPath, "pressure_low");
        var firstPage = new FolderModel { Name = "main", DisplayText = "main", UiFilePath = firstYamlPath };
        firstPage.Items.Add(firstItem);
        InvokeMainWindowSetFolders(viewModel, [firstPage]);
        var firstGeneration = viewModel.ProjectRuntimeGeneration;

        var secondItem = CreateMonitorViewTestItem("main", secondYamlPath, "temperature_high");
        var secondPage = new FolderModel { Name = "main", DisplayText = "main", UiFilePath = secondYamlPath };
        secondPage.Items.Add(secondItem);
        InvokeMainWindowSetFolders(viewModel, [secondPage]);

        var secondSelectedEntries = InvokeMonitorViewSelectedEntryResolution(
            secondItem.SelectedMonitorIds,
            InvokeMainWindowGetMonitorRegistryEntries(viewModel, secondItem));

        AssertTrue(viewModel.ProjectRuntimeGeneration > firstGeneration);
        AssertEqual(1, secondSelectedEntries.Count);
        AssertEqual("temperature_high", secondSelectedEntries[0].Name);
        AssertFalse(secondSelectedEntries.Any(static entry => string.Equals(entry.Name, "pressure_low", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        MonitorRuntimeManager.ReleaseFolder("main");
    }
}

static FolderItemModel CreateMonitorViewTestItem(string folderName, string yamlPath, string selectedMonitorIds)
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.MonitorView,
        Name = "monitor_view_1",
        SelectedMonitorIds = selectedMonitorIds
    };
    item.SetHierarchy(folderName, parentItem: null);
    item.SetLayoutFilePath(yamlPath);
    return item;
}

static MonitorRegistryEntry CreateMonitorRegistryEntry(string name, int eventId, string eventText)
    => new(
        Name: name,
        Definition: new MonitorDefinition
        {
            Name = name,
            EventId = eventId,
            EventText = eventText,
            LogLevel = MonitorLogLevel.Warning,
            SourcePath = $"signals.{name}.read"
        },
        Source: MonitorRegistrySource.CentralFile,
        SourceIdentifier: "Monitoring/Monitor.yaml");

static IReadOnlyList<MonitorRegistryEntry> InvokeMonitorViewSelectedEntryResolution(string selectedMonitorIds, IEnumerable<MonitorRegistryEntry> entries)
{
    var method = typeof(MonitorViewControl).GetMethod(
        "ResolveSelectedEntries",
        BindingFlags.NonPublic | BindingFlags.Static,
        binder: null,
        types: [typeof(string), typeof(IEnumerable<MonitorRegistryEntry>)],
        modifiers: null);
    if (method is null)
    {
        throw new InvalidOperationException("MonitorView selected entry resolver was not found.");
    }

    return (IReadOnlyList<MonitorRegistryEntry>)method.Invoke(null, [selectedMonitorIds, entries])!;
}

static void ProjectUiYamlLoaderImportsMonitorDefinitions()
{
    var yamlPath = Path.Combine(AppContext.BaseDirectory, "monitor_import_test.yaml");
    File.WriteAllText(
        yamlPath,
        """
Caption: 'main'
Screens:
  1: 'HomeScreen'
Controls:
  -
    Type: 'Monitor'
    Screen: '1'
    Enabled: true
    Identity:
      Name: 'monitor_1'
      Text: 'monitor_1'
      Path: 'monitor_1'
      Id: 'monitor-test'
    Bounds:
      X: 10
      Y: 20
      Width: 420
      Height: 220
    Properties:
      MonitorDefinitions:
        -
          name: 'monitor_rule_1'
          sourcePath: ''
          refreshRateMs: 1000
          mode: 'Custom'
          lowerLimit: ''
          upperLimit: ''
          inhibitMs: 0
          customFormula: '{A}==true'
          customVariables:
            -
              name: 'A'
              sourcePath: 'custom_signals_1.dummy_bool'
          eventId: 0
          eventText: ''
          actions:
            -
              trigger: 'OnActivated'
              actionType: 'SetValue'
              targetLog: ''
              targetPath: 'enhanced_signals.filtered_1.set'
              functionName: ''
              argument: '1000'
          targetLog: ''
          logLevel: 'Warning'
""");

    var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, "main");
    var monitorNode = layout.Layout.Children.Single(child => string.Equals(child.Type, "Monitor", StringComparison.OrdinalIgnoreCase));
    if (monitorNode.Properties["MonitorDefinitions"] is null)
    {
        throw new InvalidOperationException($"MonitorDefinitions was not mapped by the YAML loader. Properties: {monitorNode.Properties.ToJsonString()}");
    }

    var importedDefinitions = MonitorDefinitionCodec.FromJsonNode(monitorNode.Properties["MonitorDefinitions"], "main");
    if (string.IsNullOrWhiteSpace(importedDefinitions))
    {
        throw new InvalidOperationException($"MonitorDefinitions could not be decoded. Node: {monitorNode.Properties["MonitorDefinitions"]?.ToJsonString()}");
    }

    var method = typeof(MainWindowViewModel).GetMethod("ApplyKnownUiProperties", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ApplyKnownUiProperties was not found.");
    }

    var item = new FolderItemModel { Kind = ControlKind.Monitor };
    method.Invoke(null, [item, monitorNode.Properties, "main", "Monitor"]);

    var definitions = MonitorDefinitionCodec.ParseDefinitions(item.MonitorDefinitions);
    if (definitions.Count != 1)
    {
        throw new InvalidOperationException($"Expected one monitor definition, actual {definitions.Count}. Raw: {item.MonitorDefinitions}");
    }

    AssertEqual("monitor_rule_1", definitions[0].Name);
    if (definitions[0].CustomVariables.Count != 1)
    {
        throw new InvalidOperationException($"Expected one monitor variable, actual {definitions[0].CustomVariables.Count}. Raw: {item.MonitorDefinitions}");
    }

    AssertEqual("custom_signals_1.dummy_bool", definitions[0].CustomVariables[0].SourcePath);
    if (definitions[0].Actions.Count != 1)
    {
        throw new InvalidOperationException($"Expected one monitor action, actual {definitions[0].Actions.Count}. Raw: {item.MonitorDefinitions}");
    }

    AssertEqual(MonitorActionType.SetValue, definitions[0].Actions[0].ActionType);
    AssertEqual("enhanced_signals.filtered_1.set", definitions[0].Actions[0].TargetPath);
    AssertEqual("1000", definitions[0].Actions[0].Argument);
}

static void VisualRuleCodecRoundtrip()
{
    var serialized = VisualRuleCodec.SerializeDefinitions(
    [
        new VisualRule
        {
            SourceKind = VisualRuleSourceKind.MonitorRule,
            SourcePath = "studio.main.monitor.monitor_1.temperature_alarm",
            Target = VisualRuleTarget.Header,
            Property = VisualRuleProperty.BodyBackColor,
            Effect = VisualRuleEffect.Blink,
            ActiveValue = "#FF0000",
            InactiveValue = "#202020"
        }
    ]);

    var parsed = VisualRuleCodec.ParseDefinitions(serialized);
    AssertEqual(1, parsed.Count);
    AssertEqual(VisualRuleSourceKind.MonitorRule, parsed[0].SourceKind);
    AssertEqual("studio.main.monitor.monitor_1.temperature_alarm", parsed[0].SourcePath);
    AssertEqual(VisualRuleTarget.Body, parsed[0].Target);
    AssertEqual(VisualRuleProperty.BodyBackColor, parsed[0].Property);
    AssertEqual(VisualRuleEffect.Blink, parsed[0].Effect);
    AssertEqual("#FF0000", parsed[0].ActiveValue);
    AssertEqual("#202020", parsed[0].InactiveValue);
}

static void VisualRuleSourcePathDisplayHidesTechnicalMonitorPrefix()
{
    AssertEqual(
        "monitor_1.temperature_alarm",
        VisualRulesEditorDialogWindow.GetSourcePathDisplayText("studio.main.monitor.monitor_1.temperature_alarm"));
    AssertEqual(
        "monitor_1.temperature_alarm",
        VisualRulesEditorDialogWindow.GetSourcePathDisplayText("monitor.monitor_1.temperature_alarm"));
    AssertEqual(
        "runtime.sensors.pressure.value",
        VisualRulesEditorDialogWindow.GetSourcePathDisplayText("runtime.sensors.pressure.value"));
}

static void VisualRuleLayoutDocumentRoundtrip()
{
    var toDocument = typeof(MainWindowViewModel).GetMethod("ToDocument", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(FolderItemModel)], null);
    var toModel = typeof(MainWindowViewModel).GetMethod("ToModel", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(FolderItemDocument), typeof(bool)], null);
    if (toDocument is null || toModel is null)
    {
        throw new InvalidOperationException("VisualRule layout roundtrip helpers were not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "signal_1",
        VisualRules = VisualRuleCodec.SerializeDefinitions(
        [
            new VisualRule
            {
                SourceKind = VisualRuleSourceKind.MonitorRule,
                SourcePath = "studio.main.monitor.monitor_1.temperature_alarm",
                Target = VisualRuleTarget.Body,
                Property = VisualRuleProperty.BodyBackColor,
                Effect = VisualRuleEffect.None,
                ActiveValue = "#11AA22",
                InactiveValue = string.Empty
            }
        ])
    };
    item.SetHierarchy("main", parentItem: null);

    var document = (FolderItemDocument?)toDocument.Invoke(null, [item]);
    AssertTrue(document is not null);
    AssertEqual(1, document!.VisualRules.Count);
    AssertEqual("monitor.monitor_1.temperature_alarm", document.VisualRules[0].SourcePath);

    var roundtripItem = (FolderItemModel?)toModel.Invoke(null, [document, true]);
    AssertTrue(roundtripItem is not null);
    var parsed = VisualRuleCodec.ParseDefinitions(roundtripItem!.VisualRules);
    AssertEqual(1, parsed.Count);
    AssertEqual("monitor.monitor_1.temperature_alarm", parsed[0].SourcePath);
    AssertEqual(VisualRuleTarget.Body, parsed[0].Target);
    AssertEqual(VisualRuleProperty.BodyBackColor, parsed[0].Property);
    AssertEqual("#11AA22", parsed[0].ActiveValue);
}

static void ProjectUiYamlLoaderKeepsScreenDefinitionsScalarCompatible()
{
    var yamlPath = Path.Combine(AppContext.BaseDirectory, "dialog_screen_scalar_import_test.yaml");
    File.WriteAllText(
        yamlPath,
        """
Caption: 'main'
Screens:
  1: 'HomeScreen'
  2:
    Name: 'AlarmScreen'
    Kind: 'Dialog'
Controls: []
""");

    var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, "main");

    AssertEqual("HomeScreen", layout.Views[1]);
    AssertEqual("AlarmScreen", layout.Views[2]);

    var screens = layout.DocumentProperties["Screens"] as JsonObject;
    AssertTrue(screens is not null);
    AssertEqual("AlarmScreen", screens!["2"]?.GetValue<string>());
}

static void RuntimeYamlLoaderMapsDialogWidgetControls()
{
    var yamlPath = Path.Combine(AppContext.BaseDirectory, "dialog_widget_import_test.yaml");
    File.WriteAllText(
        yamlPath,
        """
Caption: 'main'
Screens:
  1: 'HomeScreen'
Controls:
  -
    Type: 'DialogWidget'
    Screen: '1'
    Enabled: true
    Identity:
      Name: 'dialog_widget_1'
      Text: 'dialog_widget_1'
      Path: 'dialog_widget_1'
      Id: 'dialog-widget-1'
    Bounds:
      X: 8
      Y: 12
      Width: 432
      Height: 308
    Rows: 4
    Columns: 5
""");

    var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, "main");
    var dialogNode = layout.Layout.Children.Single(child => string.Equals(child.Type, "DialogWidget", StringComparison.OrdinalIgnoreCase));

    var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("GetControlKindFromUiType", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetControlKindFromUiType was not found.");
    }

    var kind = method.Invoke(null, ["DialogWidget"]);
    AssertEqual(ControlKind.DialogWidget, kind);
    AssertEqual(4, dialogNode.Properties["Rows"]?.GetValue<int>());
    AssertEqual(5, dialogNode.Properties["Columns"]?.GetValue<int>());

    var applyMethod = typeof(MainWindowViewModel).GetMethod("ApplyKnownUiProperties", BindingFlags.NonPublic | BindingFlags.Static);
    if (applyMethod is null)
    {
        throw new InvalidOperationException("ApplyKnownUiProperties was not found.");
    }

    var item = new FolderItemModel { Kind = ControlKind.DialogWidget };
    applyMethod.Invoke(null, [item, dialogNode.Properties, "main", "DialogWidget"]);
    AssertEqual("dialog-widget-1", item.Id);
    AssertEqual(4, item.TableRows);
    AssertEqual(5, item.TableColumns);
}

static void RuntimeYamlLoaderMapsWorkflowWidgetControls()
{
    var yamlPath = Path.Combine(AppContext.BaseDirectory, "workflow_widget_import_test.yaml");
    File.WriteAllText(
        yamlPath,
        """
Caption: 'main'
Controls:
  -
    Type: 'WorkflowWidget'
    Screen: '1'
    Enabled: true
    Identity:
      Name: 'workflow_widget_1'
      Text: 'workflow_widget_1'
      Path: 'workflow_widget_1'
      Id: 'workflow-widget-1'
    Bounds:
      X: 16
      Y: 20
      Width: 440
      Height: 240
""");

    var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, "main");
    var workflowNode = layout.Layout.Children.Single(child => string.Equals(child.Type, "WorkflowWidget", StringComparison.OrdinalIgnoreCase));

    var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("GetControlKindFromUiType", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetControlKindFromUiType was not found.");
    }

    AssertEqual(ControlKind.Functions, method.Invoke(null, ["WorkflowWidget"]));

    var applyMethod = typeof(MainWindowViewModel).GetMethod("ApplyKnownUiProperties", BindingFlags.NonPublic | BindingFlags.Static);
    if (applyMethod is null)
    {
        throw new InvalidOperationException("ApplyKnownUiProperties was not found.");
    }

    var item = new FolderItemModel { Kind = ControlKind.Functions };
    applyMethod.Invoke(null, [item, workflowNode.Properties, "main", "WorkflowWidget"]);
    AssertEqual("workflow-widget-1", item.Id);
}

static void RuntimeYamlLoaderMapsMonitorViewControls()
{
        var yamlPath = Path.Combine(AppContext.BaseDirectory, "monitor_view_import_test.yaml");
        File.WriteAllText(
                yamlPath,
                """
Caption: 'main'
Controls:
    -
        Type: 'MonitorView'
        Screen: '1'
        Enabled: true
        Identity:
            Name: 'monitor_view_1'
            Text: 'monitor_view_1'
            Path: 'monitor_view_1'
            Id: 'monitor-view-1'
        Bounds:
            X: 16
            Y: 20
        Properties:
            SelectedMonitorIds:
                - 'monitor_rule_1'
            OnActiveColor: '#22C55E'
""");

        var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, "main");
        var monitorViewNode = layout.Layout.Children.Single(child => string.Equals(child.Type, "MonitorView", StringComparison.OrdinalIgnoreCase));

        var method = typeof(HornetStudio.ViewModels.MainWindowViewModel).GetMethod("GetControlKindFromUiType", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
                throw new InvalidOperationException("GetControlKindFromUiType was not found.");
        }

        AssertEqual(ControlKind.MonitorView, method.Invoke(null, ["MonitorView"]));

        var applyMethod = typeof(MainWindowViewModel).GetMethod("ApplyKnownUiProperties", BindingFlags.NonPublic | BindingFlags.Static);
        if (applyMethod is null)
        {
                throw new InvalidOperationException("ApplyKnownUiProperties was not found.");
        }

        var item = new FolderItemModel { Kind = ControlKind.MonitorView };
        applyMethod.Invoke(null, [item, monitorViewNode.Properties, "main", "MonitorView"]);
        AssertEqual("monitor-view-1", item.Id);
        AssertEqual("monitor_rule_1", item.SelectedMonitorIds);
        AssertEqual("#22C55E", item.OnActiveColor);
}

static void BrowserDiagnosticsSourceIncludesFolderScope()
{
    var method = typeof(EditorTemplateControl).GetMethod(
        "BuildBrowserDiagnosticsSource",
        BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildBrowserDiagnosticsSource was not found.");
    }

    var item = new FolderItemModel
    {
        Name = "EnhancedSignalsBrowser"
    };
    item.SetHierarchy("main2", parentItem: null);

    AssertEqual("EnhancedSignalsControl[main2]", method.Invoke(null, ["EnhancedSignalsControl", item]));
}

static void BrowserDiagnosticsSourceFallsBackToItemName()
{
    var method = typeof(EditorTemplateControl).GetMethod(
        "BuildBrowserDiagnosticsSource",
        BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildBrowserDiagnosticsSource was not found.");
    }

    var item = new FolderItemModel
    {
        Name = "MonitorBrowser"
    };

    AssertEqual("MonitorControl[MonitorBrowser]", method.Invoke(null, ["MonitorControl", item]));
}

static void UiDiagnosticsBacklogCountersStayNonNegative()
{
    SetUiDiagnosticsEnabledForTest(enabled: true);
    ResetUiDiagnosticsBacklogCounters();

    try
    {
        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(
            targetRefreshQueuedDelta: 2,
            targetRefreshFollowUpDelta: 1,
            targetValueRefreshQueuedDelta: 3,
            unresolvedTargetGateDelta: 4);
        UiResponsivenessDiagnostics.AdjustSignalRefreshBacklog(
            targetRefreshQueuedDelta: -10,
            targetRefreshFollowUpDelta: -10,
            targetValueRefreshQueuedDelta: -10,
            unresolvedTargetGateDelta: -10);

        var snapshot = CaptureUiDiagnosticsBacklogSnapshot();
        AssertEqual(0, GetInstanceProperty<int>(snapshot, "TargetRefreshQueued"));
        AssertEqual(0, GetInstanceProperty<int>(snapshot, "TargetRefreshFollowUpQueued"));
        AssertEqual(0, GetInstanceProperty<int>(snapshot, "TargetValueRefreshQueued"));
        AssertEqual(0, GetInstanceProperty<int>(snapshot, "UnresolvedTargetGateCount"));
    }
    finally
    {
        ResetUiDiagnosticsBacklogCounters();
        SetUiDiagnosticsEnabledForTest(enabled: false);
    }
}

static void UiDiagnosticsBenchmarkSnapshotCapturesWarmupBacklog()
{
    var session = CreateUiBenchmarkSession(
        scenario: "EditorTests",
        configuredDuration: TimeSpan.FromSeconds(30),
        warmupDuration: TimeSpan.FromSeconds(5));
    var warmupQueuedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1);
    var warmupExecutedAtUtc = warmupQueuedAtUtc.AddMilliseconds(25);

    InvokeUiBenchmarkSessionMethod(
        session,
        "RecordSignalPipelineEvent",
        "TargetRefreshExecuted",
        "studio.editor_tests.signal.m001.read",
        "m001",
        TimeSpan.FromMilliseconds(25),
        warmupQueuedAtUtc,
        warmupExecutedAtUtc);
    InvokeUiBenchmarkSessionMethod(
        session,
        "RecordSignalPipelineEvent",
        "TargetRefreshCoalesced",
        "studio.editor_tests.signal.m001.read",
        "m001",
        null,
        null,
        null);
    InvokeUiBenchmarkSessionMethod(
        session,
        "RecordSignalPipelineEvent",
        "SignalTargetUnresolved",
        "studio.editor_tests.signal.m002.read",
        "m002",
        null,
        null,
        null);

    var backlogSnapshot = CreateUiDiagnosticsBacklogSnapshot(
        targetRefreshQueued: 2,
        targetRefreshFollowUpQueued: 1,
        targetValueRefreshQueued: 3,
        unresolvedTargetGateCount: 4);
    var observedAtUtc = DateTimeOffset.UtcNow.AddSeconds(6);

    InvokeUiBenchmarkSessionMethod(
        session,
        "RecordUiDelay",
        TimeSpan.FromMilliseconds(12),
        observedAtUtc,
        backlogSnapshot);
    InvokeUiBenchmarkSessionMethod(
        session,
        "RecordUiDelay",
        TimeSpan.FromMilliseconds(10),
        observedAtUtc.AddMilliseconds(50),
        backlogSnapshot);

    var summary = InvokeUiBenchmarkSessionMethod(
        session,
        "CreateSummary",
        "EditorTests",
        observedAtUtc.AddMilliseconds(100));
    if (summary is null)
    {
        throw new InvalidOperationException("Benchmark summary was not created.");
    }

    var correlationSnapshots = (Array)GetInstanceProperty<object>(summary, "CorrelationSnapshots");
    AssertEqual(1, correlationSnapshots.Length);

    var snapshot = correlationSnapshots.GetValue(0);
    if (snapshot is null)
    {
        throw new InvalidOperationException("Correlation snapshot entry was null.");
    }

    AssertEqual("WarmupSteadyStateTransition", GetInstanceProperty<string>(snapshot, "Event"));
    AssertEqual("SteadyState", GetInstanceProperty<string>(snapshot, "Phase"));
    AssertEqual(2, GetInstanceProperty<int>(snapshot, "TargetRefreshQueued"));
    AssertEqual(1, GetInstanceProperty<int>(snapshot, "TargetRefreshFollowUpQueued"));
    AssertEqual(3, GetInstanceProperty<int>(snapshot, "TargetValueRefreshQueued"));
    AssertEqual(4, GetInstanceProperty<int>(snapshot, "UnresolvedTargetGateCount"));
    AssertTrue(GetInstanceProperty<string>(snapshot, "TargetRefreshExecuted").Contains("TargetRefreshExecuted[Warmup->Warmup]=1", StringComparison.Ordinal));
    AssertTrue(GetInstanceProperty<string>(snapshot, "TargetRefreshCoalesced").Contains("TargetRefreshCoalesced=1", StringComparison.Ordinal));
    AssertTrue(GetInstanceProperty<string>(snapshot, "SignalTargetUnresolved").Contains("SignalTargetUnresolved=1", StringComparison.Ordinal));
    AssertTrue(GetInstanceProperty<string>(snapshot, "TargetRefreshTopModules").Contains("m001", StringComparison.Ordinal));
    AssertTrue(GetInstanceProperty<string>(snapshot, "SignalTargetTopModules").Contains("m002", StringComparison.Ordinal));
}

static void CreateItemAppliesWorkflowWidgetDefaults()
{
    var viewModel = new MainWindowViewModel();
    var item = viewModel.CreateItem(ControlKind.Functions, 0, 0, 120, 120);

    AssertEqual(ControlKind.Functions, item.Kind);
    AssertEqual("Functions", item.Name);
    AssertEqual("No functions discovered", item.Footer);
    AssertTrue(item.Width >= 420);
    AssertTrue(item.Height >= 220);
}

static void MonitorViewRowUsesConfiguredActiveColor()
{
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var folderName = "main";
    var activePath = $"{MonitorRegistry.BuildRulePath(folderName, ruleName)}.active";
    var messagePath = $"{MonitorRegistry.BuildRulePath(folderName, ruleName)}.message";

    HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, true));
    HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active"));

    var ownerItem = new FolderItemModel
    {
        Kind = ControlKind.MonitorView,
        OnActiveColor = "#22C55E"
    };
    ownerItem.SetHierarchy(folderName, parentItem: null);

    var row = new MonitorViewRow(
        ownerItem,
        new MonitorDefinition
        {
            Name = ruleName,
            EventId = 1,
            EventText = "Pressure high",
            LogLevel = MonitorLogLevel.Warning
        });

    AssertTrue(row.IsActive);
    AssertEqual("#22C55E", row.RowBackground);
}

static void MonitorViewRowIgnoresUnchangedAggregateUpdates()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";

    try
    {
        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, false));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Inactive"));

        var row = CreateMonitorViewRow(folderName, ruleName);
        var changed = row.TryApplyRuntimeChange(MonitorRegistry.BuildAggregatePath(folderName));

        AssertFalse(changed);
        AssertFalse(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Inactive", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
        HostRegistries.Data.Remove(MonitorRegistry.BuildAggregatePath(folderName));
    }
}

static void MonitorViewRowIgnoresAggregateActiveChanges()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";
    var aggregatePath = MonitorRegistry.BuildAggregatePath(folderName);
    var aggregateActivePath = $"{aggregatePath}.active";

    try
    {
        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, false));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Inactive"));
        var row = CreateMonitorViewRow(folderName, ruleName);

        HostRegistries.Data.UpsertSnapshot(aggregateActivePath, ItemExtension.CreateWithPath(aggregateActivePath, true));

        var changed = row.TryApplyRuntimeChange(aggregateActivePath);

        AssertFalse(changed);
        AssertFalse(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Inactive", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
        HostRegistries.Data.Remove(aggregatePath);
        HostRegistries.Data.Remove(aggregateActivePath);
    }
}

static void MonitorViewRowAcceptsRuleRootChanges()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";

    try
    {
        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, false));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Inactive"));
        var row = CreateMonitorViewRow(folderName, ruleName);

        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, true));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active: root update"));

        var changed = row.TryApplyRuntimeChange(runtimePath);

        AssertTrue(changed);
        AssertTrue(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Active: root update", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
    }
}

static void MonitorViewRowAcceptsActiveChanges()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";

    try
    {
        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, false));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Inactive"));
        var row = CreateMonitorViewRow(folderName, ruleName);

        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, true));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active: threshold exceeded"));

        var changed = row.TryApplyRuntimeChange(activePath);

        AssertTrue(changed);
        AssertTrue(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Active: threshold exceeded", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
    }
}

static void MonitorViewRowUpdatesAfterDelayedRuntimePublish()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";

    try
    {
        var row = CreateMonitorViewRow(folderName, ruleName);
        AssertFalse(row.IsActive);

        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, true));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active: delayed publish"));

        var changed = row.TryApplyRuntimeChange(activePath);

        AssertTrue(changed);
        AssertTrue(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Active: delayed publish", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
    }
}

static void MonitorViewRowIgnoresUnselectedRuleChanges()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var selectedRuleName = $"monitor_rule_{Guid.NewGuid():N}";
    var unselectedRuleName = $"monitor_rule_{Guid.NewGuid():N}";
    var selectedRuntimePath = MonitorRegistry.BuildRulePath(folderName, selectedRuleName);
    var selectedActivePath = $"{selectedRuntimePath}.active";
    var selectedMessagePath = $"{selectedRuntimePath}.message";
    var unselectedRuntimePath = MonitorRegistry.BuildRulePath(folderName, unselectedRuleName);
    var unselectedActivePath = $"{unselectedRuntimePath}.active";
    var unselectedMessagePath = $"{unselectedRuntimePath}.message";

    try
    {
        HostRegistries.Data.UpsertSnapshot(selectedActivePath, ItemExtension.CreateWithPath(selectedActivePath, false));
        HostRegistries.Data.UpsertSnapshot(selectedMessagePath, ItemExtension.CreateWithPath(selectedMessagePath, "Inactive"));
        var row = CreateMonitorViewRow(folderName, selectedRuleName);

        HostRegistries.Data.UpsertSnapshot(unselectedActivePath, ItemExtension.CreateWithPath(unselectedActivePath, true));
        HostRegistries.Data.UpsertSnapshot(unselectedMessagePath, ItemExtension.CreateWithPath(unselectedMessagePath, "Active: unrelated"));

        var changed = row.TryApplyRuntimeChange(unselectedActivePath);

        AssertFalse(changed);
        AssertFalse(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Inactive", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(selectedRuntimePath);
        HostRegistries.Data.Remove(selectedActivePath);
        HostRegistries.Data.Remove(selectedMessagePath);
        HostRegistries.Data.Remove(unselectedRuntimePath);
        HostRegistries.Data.Remove(unselectedActivePath);
        HostRegistries.Data.Remove(unselectedMessagePath);
    }
}

static void MonitorViewRowAcceptsMessageChanges()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";

    try
    {
        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, true));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active: first detail"));
        var row = CreateMonitorViewRow(folderName, ruleName);

        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active: updated detail"));

        var changed = row.TryApplyRuntimeChange(messagePath);

        AssertTrue(changed);
        AssertTrue(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Active: updated detail", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
    }
}

static void MonitorViewRowIgnoresRedundantMessageUpdates()
{
    var folderName = $"main_{Guid.NewGuid():N}";
    var ruleName = $"monitor_rule_{Guid.NewGuid():N}";
    var runtimePath = MonitorRegistry.BuildRulePath(folderName, ruleName);
    var activePath = $"{runtimePath}.active";
    var messagePath = $"{runtimePath}.message";

    try
    {
        HostRegistries.Data.UpsertSnapshot(activePath, ItemExtension.CreateWithPath(activePath, true));
        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active"));
        var row = CreateMonitorViewRow(folderName, ruleName);

        HostRegistries.Data.UpsertSnapshot(messagePath, ItemExtension.CreateWithPath(messagePath, "Active"));

        var changed = row.TryApplyRuntimeChange(messagePath);

        AssertFalse(changed);
        AssertTrue(row.IsActive);
        AssertTrue(row.RowTooltip.Contains("Active", StringComparison.Ordinal));
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
        HostRegistries.Data.Remove(activePath);
        HostRegistries.Data.Remove(messagePath);
    }
}

static MonitorViewRow CreateMonitorViewRow(string folderName, string ruleName)
{
    var ownerItem = new FolderItemModel { Kind = ControlKind.MonitorView };
    ownerItem.SetHierarchy(folderName, parentItem: null);
    return new MonitorViewRow(
        ownerItem,
        new MonitorDefinition
        {
            Name = ruleName,
            EventId = 1,
            EventText = "Pressure high",
            LogLevel = MonitorLogLevel.Warning
        });
}

static void CanvasWidgetPickerExcludesFunctions()
{
    var method = typeof(FolderEditorControl).GetMethod("CreateCanvasWidgetSelectionItems", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("CreateCanvasWidgetSelectionItems was not found.");
    }

    var control = new FolderEditorControl
    {
        DataContext = new MainWindowViewModel()
    };

    var items = method.Invoke(control, null) as System.Collections.IEnumerable;
    if (items is null)
    {
        throw new InvalidOperationException("Widget selection items were not returned.");
    }

    foreach (var item in items)
    {
        var kindProperty = item.GetType().GetProperty("Kind", BindingFlags.Public | BindingFlags.Instance);
        if (kindProperty?.GetValue(item) is ControlKind kind && kind == ControlKind.Functions)
        {
            throw new InvalidOperationException("Functions must not be offered in the canvas widget picker.");
        }
    }
}

static void AdminBrowserVisibilityFollowsCurrentUserLevel()
{
    var viewModel = new MainWindowViewModel();

    AssertFalse(viewModel.IsAdminBrowserVisible);

    AssertTrue(viewModel.TrySetUserByPassword("admin"));
    AssertTrue(viewModel.IsAdminBrowserVisible);

    AssertTrue(viewModel.TrySetUserByPassword(string.Empty));
    AssertFalse(viewModel.IsAdminBrowserVisible);
}

static void DialogInteractionRulesPersistDialogWidgetIds()
{
    var method = typeof(MainWindowViewModel).GetMethod("BuildYamlControlDefinition", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildYamlControlDefinition was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1",
        InteractionRules = ItemInteractionRuleCodec.SerializeDefinitions(
        [
            new ItemInteractionRule
            {
                Event = ItemInteractionEvent.BodyLeftClick,
                Action = ItemInteractionAction.OpenDialog,
                TargetPath = "dialog-widget-1 - Alarm Dialog (main)",
                Argument = "Screen,Center"
            }
        ])
    };
    item.SetHierarchy("main", parentItem: null);

    var node = (JsonObject?)method.Invoke(null, [item]);
    var interactionRules = node?["InteractionRules"] as JsonArray;
    AssertTrue(interactionRules is not null);
    AssertEqual("dialog-widget-1", interactionRules![0]?["TargetPath"]?.GetValue<string>());
}

static void StopFunctionInteractionRulesPersistFunctionName()
{
    var method = typeof(MainWindowViewModel).GetMethod("BuildYamlControlDefinition", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildYamlControlDefinition was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1",
        InteractionRules = ItemInteractionRuleCodec.SerializeDefinitions(
        [
            new ItemInteractionRule
            {
                Event = ItemInteractionEvent.BodyLeftClick,
                Action = ItemInteractionAction.StopFunction,
                TargetPath = "this",
                FunctionName = "yaml:new_workflow",
                Argument = string.Empty
            }
        ])
    };
    item.SetHierarchy("main", parentItem: null);

    var node = (JsonObject?)method.Invoke(null, [item]);
    var interactionRules = node?["InteractionRules"] as JsonArray;
    AssertTrue(interactionRules is not null);
    AssertEqual("StopFunction", interactionRules![0]?["Action"]?.GetValue<string>());
    AssertEqual("yaml:new_workflow", interactionRules[0]?["FunctionName"]?.GetValue<string>());
    AssertEqual("this", interactionRules[0]?["TargetPath"]?.GetValue<string>());
}

static void DialogWidgetPickerOnlyListsDialogWidgets()
{
    var viewModel = new MainWindowViewModel();
    var page = viewModel.SelectedFolder;
    var button = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1"
    };
    var dialog = new FolderItemModel
    {
        Kind = ControlKind.DialogWidget,
        Id = "dialog-widget-1",
        Name = "alarm_dialog",
        ControlCaption = "Alarm Dialog"
    };
    page.Items.Add(button);
    page.Items.Add(dialog);

    var method = typeof(MainWindowViewModel).GetMethod("GetSelectableDialogWidgetOptions", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("GetSelectableDialogWidgetOptions was not found.");
    }

    var options = ((IEnumerable<string>?)method.Invoke(viewModel, [button]))?.ToArray() ?? [];
    AssertEqual(1, options.Length);
    AssertTrue(!string.IsNullOrWhiteSpace(options[0]));
}

static void CreateItemAppliesRequestedCaptionVisibilityDefaults()
{
    var viewModel = new MainWindowViewModel();

    var signal = viewModel.CreateItem(ControlKind.Signal, 0, 0, 200, 72);
    var button = viewModel.CreateItem(ControlKind.Button, 0, 0, 200, 72);
    var table = viewModel.CreateItem(ControlKind.TableControl, 0, 0, 260, 220);
    var chart = viewModel.CreateItem(ControlKind.ChartControl, 0, 0, 520, 260);
    var itemClient = viewModel.CreateItem(ControlKind.ItemClient, 0, 0, 420, 190);
    var dialog = viewModel.CreateItem(ControlKind.DialogWidget, 0, 0, 420, 260);

    AssertFalse(signal.BodyCaptionVisible);
    AssertFalse(button.CaptionVisible);
    AssertFalse(table.BodyCaptionVisible);
    AssertFalse(chart.BodyCaptionVisible);
    AssertFalse(itemClient.BodyCaptionVisible);
    AssertFalse(dialog.BodyCaptionVisible);
}

static void OpenDialogKeepsOneOverlayPerDialogWidget()
{
    var viewModel = new MainWindowViewModel();
    var page = viewModel.SelectedFolder;
    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1"
    };
    var dialog = new FolderItemModel
    {
        Kind = ControlKind.DialogWidget,
        Id = "dialog-widget-1",
        Name = "alarm_dialog",
        ControlCaption = "Alarm Dialog"
    };
    page.Items.Add(item);
    page.Items.Add(dialog);

    AssertTrue(viewModel.OpenDialogWidget("dialog-widget-1", null, item, out var openError));
    AssertEqual(string.Empty, openError);
    AssertEqual(1, viewModel.OpenDialogOverlays.Count);
    AssertEqual("dialog-widget-1", viewModel.OpenDialogOverlays[0].DialogWidgetId);

    AssertTrue(viewModel.OpenDialogWidget("dialog-widget-1", null, item, out openError));
    AssertEqual(string.Empty, openError);
    AssertEqual(1, viewModel.OpenDialogOverlays.Count);

    AssertTrue(viewModel.CloseDialogWidget("dialog-widget-1", item, out var closeError));
    AssertEqual(string.Empty, closeError);
    AssertEqual(0, viewModel.OpenDialogOverlays.Count);
}

static void OpenDialogAppliesDefaultPlacement()
{
    var viewModel = new MainWindowViewModel();
    var page = viewModel.SelectedFolder;
    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1"
    };
    var dialog = new FolderItemModel
    {
        Kind = ControlKind.DialogWidget,
        Id = "dialog-widget-1",
        Name = "alarm_dialog"
    };
    page.Items.Add(item);
    page.Items.Add(dialog);

    AssertTrue(viewModel.OpenDialogWidget("dialog-widget-1", null, item, out var openError));
    AssertEqual(string.Empty, openError);
    AssertEqual("Screen", viewModel.OpenDialogOverlays[0].Origin);
    AssertEqual("Center", viewModel.OpenDialogOverlays[0].Position);
}

static void OpenDialogUsesDialogWidgetBounds()
{
    var viewModel = new MainWindowViewModel();
    var page = viewModel.SelectedFolder;
    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1"
    };
    var dialog = new FolderItemModel
    {
        Kind = ControlKind.DialogWidget,
        Id = "dialog-widget-1",
        Name = "alarm_dialog",
        Title = "Alarm Dialog",
        Width = 432,
        Height = 308,
        TableRows = 2,
        TableColumns = 2
    };
    dialog.Items.Add(new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "dialog_signal",
        TableCellRow = 1,
        TableCellColumn = 1
    });
    page.Items.Add(item);
    page.Items.Add(dialog);

    AssertTrue(viewModel.OpenDialogWidget("dialog-widget-1", null, item, out var openError));
    AssertEqual(string.Empty, openError);
    AssertEqual(432d, viewModel.OpenDialogOverlays[0].ContentWidth);
    AssertEqual(308d, viewModel.OpenDialogOverlays[0].ContentHeight);
    AssertEqual("dialog-widget-1", viewModel.OpenDialogOverlays[0].DialogItem.Id);
    AssertFalse(viewModel.OpenDialogOverlays[0].DialogItem.ShowControlCaption);
    AssertTrue(dialog.ShowControlCaption);
    AssertEqual(1, viewModel.OpenDialogOverlays[0].DialogItem.Items.Count);
    AssertTrue(viewModel.OpenDialogOverlays[0].DialogItem.Items[0].IsTableChildControl);
}

static void OpenDialogPreservesDialogGridChildPlacement()
{
    var viewModel = new MainWindowViewModel();
    var page = viewModel.SelectedFolder;
    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1"
    };
    var dialog = new FolderItemModel
    {
        Kind = ControlKind.DialogWidget,
        Id = "dialog-widget-1",
        Name = "alarm_dialog",
        Width = 432,
        Height = 308,
        TableRows = 2,
        TableColumns = 2
    };
    dialog.Items.Add(new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "dialog_signal",
        TableCellRow = 1,
        TableCellColumn = 1,
        TableCellRowSpan = 1,
        TableCellColumnSpan = 2
    });
    dialog.Items.Add(new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_3",
        TableCellRow = 2,
        TableCellColumn = 1,
        TableCellRowSpan = 1,
        TableCellColumnSpan = 2
    });
    page.Items.Add(item);
    page.Items.Add(dialog);

    AssertTrue(viewModel.OpenDialogWidget("dialog-widget-1", null, item, out var openError));
    AssertEqual(string.Empty, openError);
    AssertEqual(1, viewModel.OpenDialogOverlays.Count);

    var overlayDialog = viewModel.OpenDialogOverlays[0].DialogItem;
    AssertEqual(2, overlayDialog.Items.Count);

    AssertEqual("dialog_signal", overlayDialog.Items[0].Name);
    AssertEqual(1, overlayDialog.Items[0].TableCellRow);
    AssertEqual(1, overlayDialog.Items[0].TableCellColumn);
    AssertEqual(1, overlayDialog.Items[0].TableCellRowSpan);
    AssertEqual(2, overlayDialog.Items[0].TableCellColumnSpan);

    AssertEqual("button_3", overlayDialog.Items[1].Name);
    AssertEqual(2, overlayDialog.Items[1].TableCellRow);
    AssertEqual(1, overlayDialog.Items[1].TableCellColumn);
    AssertEqual(1, overlayDialog.Items[1].TableCellRowSpan);
    AssertEqual(2, overlayDialog.Items[1].TableCellColumnSpan);
}

static void SetValueInlineOptionsMatchTargetKind()
{
    var numericOptions = SetValueOperationCodec.GetInlineOperationOptions(SetValueTargetKind.Numeric).ToArray();
    AssertEqual(4, numericOptions.Length);
    AssertEqual(SetValueOperationKind.SetLiteral, numericOptions[0].Kind);
    AssertEqual("=", numericOptions[0].DisplayText);
    AssertEqual(SetValueOperationKind.IncrementBy, numericOptions[1].Kind);
    AssertEqual("+", numericOptions[1].DisplayText);
    AssertEqual(SetValueOperationKind.DecrementBy, numericOptions[2].Kind);
    AssertEqual("-", numericOptions[2].DisplayText);
    AssertEqual(SetValueOperationKind.SetFromItem, numericOptions[3].Kind);
    AssertEqual("Item", numericOptions[3].DisplayText);
    AssertTrue(numericOptions[3].UsesSourceItem);

    var booleanOptions = SetValueOperationCodec.GetInlineOperationOptions(SetValueTargetKind.Boolean).ToArray();
    AssertEqual(3, booleanOptions.Length);
    AssertEqual(SetValueOperationKind.SetTrue, booleanOptions[0].Kind);
    AssertEqual("true", booleanOptions[0].DisplayText);
    AssertEqual(SetValueOperationKind.SetFalse, booleanOptions[1].Kind);
    AssertEqual("false", booleanOptions[1].DisplayText);
    AssertEqual(SetValueOperationKind.SetFromItem, booleanOptions[2].Kind);
    AssertEqual("Item", booleanOptions[2].DisplayText);
    AssertTrue(booleanOptions[2].UsesSourceItem);

    var stringOptions = SetValueOperationCodec.GetInlineOperationOptions(SetValueTargetKind.String).ToArray();
    AssertEqual(3, stringOptions.Length);
    AssertEqual(SetValueOperationKind.SetLiteral, stringOptions[0].Kind);
    AssertEqual(SetValueOperationKind.AppendText, stringOptions[1].Kind);
    AssertEqual("+", stringOptions[1].DisplayText);
    AssertEqual(SetValueOperationKind.SetFromItem, stringOptions[2].Kind);
}

static void SetValueInlineEditorMapsParseableBooleanLiterals()
{
    var legacyTrue = SetValueOperationCodec.ToInlineEditorOperation(
        new SetValueOperation
        {
            Kind = SetValueOperationKind.SetLiteral,
            LiteralValue = "1",
            IsLegacyLiteral = true
        },
        SetValueTargetKind.Boolean);
    AssertEqual(SetValueOperationKind.SetTrue, legacyTrue.Kind);

    var legacyFalse = SetValueOperationCodec.ToInlineEditorOperation(
        new SetValueOperation
        {
            Kind = SetValueOperationKind.SetLiteral,
            LiteralValue = "false",
            IsLegacyLiteral = true
        },
        SetValueTargetKind.Boolean);
    AssertEqual(SetValueOperationKind.SetFalse, legacyFalse.Kind);

    var invalidLiteral = SetValueOperationCodec.ToInlineEditorOperation(
        new SetValueOperation
        {
            Kind = SetValueOperationKind.SetLiteral,
            LiteralValue = "maybe",
            IsLegacyLiteral = true
        },
        SetValueTargetKind.Boolean);
    AssertEqual(SetValueOperationKind.SetLiteral, invalidLiteral.Kind);
    AssertEqual("maybe", invalidLiteral.LiteralValue);
}

static void SetValueInlineEditorRowSerializesNumericDelta()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.SetValue)
    };

    row.SetValueTargetKind = SetValueTargetKind.Numeric;
    row.SelectedSetValueOperation = row.SetValueOperationOptions.Single(option => option.Kind == SetValueOperationKind.IncrementBy);
    row.SetValueLiteralArgument = "18";

    var parsed = SetValueOperationCodec.Parse(row.Argument);
    AssertTrue(parsed.IsValid);
    AssertEqual(SetValueOperationKind.IncrementBy, parsed.Operation.Kind);
    AssertEqual("18", parsed.Operation.LiteralValue);
}

static void SetValueInlineEditorRowSerializesBooleanTrue()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.SetValue)
    };

    row.SetValueTargetKind = SetValueTargetKind.Boolean;
    row.SelectedSetValueOperation = row.SetValueOperationOptions.Single(option => option.Kind == SetValueOperationKind.SetTrue);

    var parsed = SetValueOperationCodec.Parse(row.Argument);
    AssertTrue(parsed.IsValid);
    AssertEqual(SetValueOperationKind.SetTrue, parsed.Operation.Kind);
    AssertFalse(row.ShowsSetValueLiteralEditor);
    AssertFalse(row.ShowsSetValueSourceEditor);
}

static void SetValueInlineEditorRowPreservesInvalidLegacyBooleanLiteral()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.SetValue),
        Argument = "maybe"
    };

    row.SetValueTargetKind = SetValueTargetKind.Boolean;

    AssertTrue(row.SelectedSetValueOperation is not null);
    AssertEqual(SetValueOperationKind.SetTrue, row.SelectedSetValueOperation!.Kind);
    AssertEqual("maybe", row.SetValueLiteralArgument);
    AssertFalse(row.ShowsSetValueLiteralEditor);
}

static void SetValueInlineEditorRowSerializesStringAppendSeparator()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.SetValue)
    };

    row.SetValueTargetKind = SetValueTargetKind.String;
    row.SelectedSetValueOperation = row.SetValueOperationOptions.Single(option => option.Kind == SetValueOperationKind.AppendText);
    row.SetValueLiteralArgument = "B";
    row.SetValueSeparator = ", ";

    var parsed = SetValueOperationCodec.Parse(row.Argument);
    AssertTrue(parsed.IsValid);
    AssertEqual(SetValueOperationKind.AppendText, parsed.Operation.Kind);
    AssertEqual("B", parsed.Operation.LiteralValue);
    AssertEqual(", ", parsed.Operation.Separator);
    AssertTrue(row.ShowsSetValueLiteralEditor);
    AssertTrue(row.ShowsSetValueSeparatorEditor);
}

static void SetValueInlineEditorRowReportsInvalidNumericLiteral()
{
    const string targetPath = "udl1.m001.set";
    var item = ItemExtension.CreateWithPath(targetPath, 1.25f);
    item.Properties["write"].Value = 1.25f;
    item.Properties["type"].Value = "float";
    HostRegistries.Data.UpsertSnapshot(targetPath, item);

    try
    {
        var field = CreateInteractionRuleField();
        var row = new ItemInteractionEditorRow
        {
            ActionName = nameof(ItemInteractionAction.SetValue),
            TargetPath = targetPath
        };

        row.SetValueTargetKind = SetValueTargetKind.Numeric;
        row.SelectedSetValueOperation = row.SetValueOperationOptions.Single(option => option.Kind == SetValueOperationKind.IncrementBy);
        row.SetValueLiteralArgument = "abc";

        field.RefreshSetValueMetadata(row);

        AssertTrue(row.HasSetValueValidationError);
        AssertEqual("Enter a valid numeric delta using invariant format, for example 1 or 0.5.", row.SetValueValidationMessage);
    }
    finally
    {
        HostRegistries.Data.Remove(targetPath);
    }
}

static void SetValueInlineEditorRowLoadsLegacyLiteralAsEquals()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.SetValue),
        Argument = "hello"
    };

    row.SetValueTargetKind = SetValueTargetKind.String;

    AssertTrue(row.SelectedSetValueOperation is not null);
    AssertEqual(SetValueOperationKind.SetLiteral, row.SelectedSetValueOperation!.Kind);
    AssertEqual("hello", row.SetValueLiteralArgument);
    AssertTrue(row.ShowsSetValueLiteralEditor);
}

static void SetValueInlineEditorRowUsesItemSourcePath()
{
    var row = new ItemInteractionEditorRow
    {
        ActionName = nameof(ItemInteractionAction.SetValue)
    };

    row.SetValueTargetKind = SetValueTargetKind.Numeric;
    row.SetSetValueSourceOptions(["studio.main.source.read"]);
    row.SelectedSetValueOperation = row.SetValueOperationOptions.Single(option => option.Kind == SetValueOperationKind.SetFromItem);
    row.SetValueSourcePath = "studio.main.source.read";

    var parsed = SetValueOperationCodec.Parse(row.Argument);
    AssertTrue(parsed.IsValid);
    AssertEqual(SetValueOperationKind.SetFromItem, parsed.Operation.Kind);
    AssertEqual("studio.main.source.read", parsed.Operation.SourcePath);
    AssertTrue(row.ShowsSetValueSourceEditor);
}

static void SetValueRuntimeAppendInsertsSeparatorOnlyWhenNeeded()
{
    const string targetPath = "studio.main.output.text";
    var targetItem = ItemExtension.CreateWithPath(targetPath, "A");
    targetItem.Properties["write"].Value = "A";
    targetItem.Properties["type"].Value = "string";

    var model = new FolderItemModel();
    var method = typeof(FolderItemModel).GetMethod(
        "TryResolveSetValueOperationValue",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(SetValueOperation), typeof(string), typeof(ItemModel), typeof(object).MakeByRefType(), typeof(string).MakeByRefType()],
        modifiers: null);
    if (method is null)
    {
        throw new InvalidOperationException("TryResolveSetValueOperationValue was not found.");
    }

    var appendWithSeparator = new SetValueOperation
    {
        Kind = SetValueOperationKind.AppendText,
        LiteralValue = "B",
        Separator = ", "
    };
    var arguments = new object?[] { appendWithSeparator, targetPath, targetItem, null, null };
    AssertEqual(true, method.Invoke(model, arguments));
    AssertEqual("A, B", arguments[3]);
    AssertEqual(string.Empty, arguments[4]);

    targetItem.Properties["write"].Value = string.Empty;
    arguments = [appendWithSeparator, targetPath, targetItem, null, null];
    AssertEqual(true, method.Invoke(model, arguments));
    AssertEqual("B", arguments[3]);

    targetItem.Properties["write"].Value = "A";
    var appendEmpty = new SetValueOperation
    {
        Kind = SetValueOperationKind.AppendText,
        LiteralValue = string.Empty,
        Separator = ", "
    };
    arguments = [appendEmpty, targetPath, targetItem, null, null];
    AssertEqual(true, method.Invoke(model, arguments));
    AssertEqual("A", arguments[3]);
}

static void ProjectUiYamlLoaderImportsVisualRules()
{
    var yamlPath = Path.Combine(AppContext.BaseDirectory, "visual_rule_import_test.yaml");
    File.WriteAllText(
        yamlPath,
        """
Caption: 'main'
Screens:
  1: 'HomeScreen'
Controls:
  -
    Type: 'Signal'
    Screen: '1'
    Enabled: true
    Identity:
      Name: 'signal_1'
      Text: 'signal_1'
      Path: 'signal_1'
      Id: 'signal-test'
    Bounds:
      X: 10
      Y: 20
      Width: 220
      Height: 120
    VisualRules:
      -
        SourceKind: 'MonitorRule'
        SourcePath: 'studio.main.monitor.monitor_1.temperature_alarm'
        Target: 'Body'
        Property: 'Background'
        Effect: 'Blink'
        ActiveValue: '#FFAA00'
        InactiveValue: ''
""");

    var layout = ProjectUiLayoutLoader.LoadYaml(yamlPath, "main");
    var signalNode = layout.Layout.Children.Single(child => string.Equals(child.Type, "Signal", StringComparison.OrdinalIgnoreCase));
    if (signalNode.Properties["VisualRules"] is null)
    {
        throw new InvalidOperationException($"VisualRules was not mapped by the YAML loader. Properties: {signalNode.Properties.ToJsonString()}");
    }

    var method = typeof(MainWindowViewModel).GetMethod("ApplyKnownUiProperties", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ApplyKnownUiProperties was not found.");
    }

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    method.Invoke(null, [item, signalNode.Properties, "main", "Signal"]);

    var rules = VisualRuleCodec.ParseDefinitions(item.VisualRules);
    AssertEqual(1, rules.Count);
    AssertEqual("studio.main.monitor.monitor_1.temperature_alarm", rules[0].SourcePath);
    AssertEqual(VisualRuleTarget.Body, rules[0].Target);
    AssertEqual(VisualRuleProperty.BodyBackColor, rules[0].Property);
    AssertEqual(VisualRuleEffect.Blink, rules[0].Effect);
    AssertEqual("#FFAA00", rules[0].ActiveValue);
}

static void SignalVisualRuleAppliesBodyBackColor()
{
    var sourcePath = "studio.editor_tests.visual_rules.signal_active";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, true));

    var item = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "signal_1",
        VisualRules = VisualRuleCodec.SerializeDefinitions(
        [
            new VisualRule
            {
                SourceKind = VisualRuleSourceKind.MonitorRule,
                SourcePath = sourcePath,
                Property = VisualRuleProperty.BodyBackColor,
                Effect = VisualRuleEffect.None,
                ActiveValue = "#112233"
            }
        ])
    };

    item.SetHierarchy("main", parentItem: null);
    item.ApplyTheme(isDarkTheme: false);

    AssertEqual("#112233", item.EffectiveBodyBackground);
}

static void ButtonVisualRuleAppliesButtonBackColor()
{
    var sourcePath = "studio.editor_tests.visual_rules.button_active";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, true));

    var item = new FolderItemModel
    {
        Kind = ControlKind.Button,
        Name = "button_1",
        ButtonBodyBackground = "#556677",
        VisualRules = VisualRuleCodec.SerializeDefinitions(
        [
            new VisualRule
            {
                SourceKind = VisualRuleSourceKind.MonitorRule,
                SourcePath = sourcePath,
                Property = VisualRuleProperty.ButtonBackColor,
                Effect = VisualRuleEffect.None,
                ActiveValue = "#334455"
            }
        ])
    };

    item.SetHierarchy("main", parentItem: null);
    item.ApplyTheme(isDarkTheme: false);

    AssertEqual("#334455", item.EffectiveButtonBodyBackground);
    AssertEqual("Transparent", item.EffectiveBodyBackground);
}

static void CircleDisplayVisualRuleAppliesDisplayBackColor()
{
    var sourcePath = "studio.editor_tests.visual_rules.circle_active";
    HostRegistries.Data.UpsertSnapshot(sourcePath, ItemExtension.CreateWithPath(sourcePath, true));

    var item = new FolderItemModel
    {
        Kind = ControlKind.CircleDisplay,
        Name = "circle_1",
        VisualRules = VisualRuleCodec.SerializeDefinitions(
        [
            new VisualRule
            {
                SourceKind = VisualRuleSourceKind.MonitorRule,
                SourcePath = sourcePath,
                Property = VisualRuleProperty.DisplayBackColor,
                Effect = VisualRuleEffect.None,
                ActiveValue = "#778899"
            }
        ])
    };

    item.SetHierarchy("main", parentItem: null);
    item.ApplyTheme(isDarkTheme: false);

    AssertEqual("#778899", item.EffectiveDisplayBackColor);
}

static void MonitorSetValueTransitionActionWritesTargetValue()
{
    var sourcePath = "studio.editor_tests.monitor.source";
    var targetPath = "studio.editor_tests.monitor.target";

    var source = ItemExtension.CreateWithPath(sourcePath, 1d);
    HostRegistries.Data.UpsertSnapshot(sourcePath, source);

    var target = ItemExtension.CreateWithPath(targetPath, 0d);
    target.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, target);

    var ownerItem = new FolderItemModel
    {
        Name = "monitor_widget"
    };

    using var row = new MonitorRuleRow(
        ownerItem,
        new MonitorDefinition
        {
            Name = "target_write",
            SourcePath = sourcePath,
            Mode = MonitorRuleMode.Default,
            LowerLimit = "2",
            Actions =
            [
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnActivated,
                    ActionType = MonitorActionType.SetValue,
                    TargetPath = targetPath,
                    Argument = "42"
                }
            ]
        },
        static () => { });

    AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var resolved));
    AssertEqual(42d, resolved?.Value);
}

static void MonitorSetValueClearActionRemainsStableForIndependentTarget()
{
    var sourcePath = "studio.editor_tests.monitor.clear_source";
    var targetPath = "studio.editor_tests.monitor.clear_target";

    var source = ItemExtension.CreateWithPath(sourcePath, true);
    HostRegistries.Data.UpsertSnapshot(sourcePath, source);

    var target = ItemExtension.CreateWithPath(targetPath, 0d);
    target.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, target);

    var ownerItem = new FolderItemModel
    {
        Name = "monitor_widget"
    };

    using var row = new MonitorRuleRow(
        ownerItem,
        new MonitorDefinition
        {
            Name = "independent_target_write",
            SourcePath = sourcePath,
            Mode = MonitorRuleMode.Custom,
            CustomFormula = "{A} == true",
            CustomVariables =
            [
                new MonitorVariableDefinition
                {
                    Name = "A",
                    SourcePath = sourcePath
                }
            ],
            Actions =
            [
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnActivated,
                    ActionType = MonitorActionType.SetValue,
                    TargetPath = targetPath,
                    Argument = "1000"
                },
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnCleared,
                    ActionType = MonitorActionType.SetValue,
                    TargetPath = targetPath,
                    Argument = "0"
                }
            ]
        },
        static () => { });

    AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var activeTarget));
    AssertEqual(1000d, activeTarget?.Value);

    AssertTrue(HostRegistries.Data.UpdateValue(sourcePath, false));
    row.Evaluate();

    AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var clearedTarget));
    AssertEqual(0d, clearedTarget?.Value);

    row.Evaluate();

    AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var stableTarget));
    AssertEqual(0d, stableTarget?.Value);
}

static void MonitorAggregateMetadataIncludesActiveEventTexts()
{
    var warningSourceAPath = "studio.editor_tests.monitor.aggregate.warning_a";
    var warningSourceBPath = "studio.editor_tests.monitor.aggregate.warning_b";
    var warningInactivePath = "studio.editor_tests.monitor.aggregate.warning_inactive";
    var errorSourcePath = "studio.editor_tests.monitor.aggregate.error";

    HostRegistries.Data.UpsertSnapshot(warningSourceAPath, ItemExtension.CreateWithPath(warningSourceAPath, 1d));
    HostRegistries.Data.UpsertSnapshot(warningSourceBPath, ItemExtension.CreateWithPath(warningSourceBPath, 2d));
    HostRegistries.Data.UpsertSnapshot(warningInactivePath, ItemExtension.CreateWithPath(warningInactivePath, 10d));
    HostRegistries.Data.UpsertSnapshot(errorSourcePath, ItemExtension.CreateWithPath(errorSourcePath, 1d));

    try
    {
        var ownerItem = new FolderItemModel
        {
            Name = "monitor_widget"
        };

        using var warningRowA = new MonitorRuleRow(
            ownerItem,
            new MonitorDefinition
            {
                Name = "alarm_a",
                SourcePath = warningSourceAPath,
                Mode = MonitorRuleMode.Default,
                LowerLimit = "5",
                EventId = 123,
                EventText = "RangeError",
                LogLevel = MonitorLogLevel.Warning
            },
            static () => { });

        using var warningRowB = new MonitorRuleRow(
            ownerItem,
            new MonitorDefinition
            {
                Name = "alarm_b",
                SourcePath = warningSourceBPath,
                Mode = MonitorRuleMode.Default,
                LowerLimit = "5",
                EventId = 124,
                EventText = "DI5 high",
                LogLevel = MonitorLogLevel.Warning
            },
            static () => { });

        using var warningInactiveRow = new MonitorRuleRow(
            ownerItem,
            new MonitorDefinition
            {
                Name = "alarm_c",
                SourcePath = warningInactivePath,
                Mode = MonitorRuleMode.Default,
                LowerLimit = "5",
                EventId = 125,
                EventText = "Ignored",
                LogLevel = MonitorLogLevel.Warning
            },
            static () => { });

        using var errorRow = new MonitorRuleRow(
            ownerItem,
            new MonitorDefinition
            {
                Name = "error_a",
                SourcePath = errorSourcePath,
                Mode = MonitorRuleMode.Default,
                LowerLimit = "5",
                EventId = 500,
                EventText = "FatalError",
                LogLevel = MonitorLogLevel.Error
            },
            static () => { });

        var aggregates = MonitorControl.BuildActiveEventIdAggregates([warningRowB, warningInactiveRow, errorRow, warningRowA]);
        var warningAggregate = aggregates.Single(static aggregate => string.Equals(aggregate.ItemName, "warning_active", StringComparison.OrdinalIgnoreCase));
        var errorAggregate = aggregates.Single(static aggregate => string.Equals(aggregate.ItemName, "error_active", StringComparison.OrdinalIgnoreCase));
        var debugAggregate = aggregates.Single(static aggregate => string.Equals(aggregate.ItemName, "debug_active", StringComparison.OrdinalIgnoreCase));

        AssertEqual("123,124", warningAggregate.EventIds);
        AssertEqual("500", errorAggregate.EventIds);
        AssertEqual(string.Empty, debugAggregate.EventIds);

        using var warningMeta = JsonDocument.Parse(warningAggregate.MetaJson);
        var warningEvents = warningMeta.RootElement.GetProperty("events");
        AssertEqual(2, warningEvents.GetArrayLength());
        AssertEqual(123, warningEvents[0].GetProperty("event_id").GetInt32());
        AssertEqual("RangeError", warningEvents[0].GetProperty("text").GetString());
        AssertEqual(124, warningEvents[1].GetProperty("event_id").GetInt32());
        AssertEqual("DI5 high", warningEvents[1].GetProperty("text").GetString());

        using var debugMeta = JsonDocument.Parse(debugAggregate.MetaJson);
        AssertEqual(0, debugMeta.RootElement.GetProperty("events").GetArrayLength());
    }
    finally
    {
        HostRegistries.Data.Remove(warningSourceAPath);
        HostRegistries.Data.Remove(warningSourceBPath);
        HostRegistries.Data.Remove(warningInactivePath);
        HostRegistries.Data.Remove(errorSourcePath);
    }
}

static void MonitorControlIgnoresNonMonitorItems()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.ApplicationExplorer,
        Name = "application_explorer_1"
    };
    item.SetHierarchy("monitor_guard_test", parentItem: null);

    var aggregatePath = MonitorRuleRow.BuildMonitorRegistryPath(item.FolderName, item.Name);
    HostRegistries.Data.Remove(aggregatePath);

    try
    {
        var method = typeof(MonitorControl).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(methodInfo => string.Equals(methodInfo.Name, "PublishAggregateRuntime", StringComparison.Ordinal)
                && methodInfo.GetParameters().Length == 2);

        var published = (bool)method.Invoke(null, [item, Array.Empty<MonitorRuleRow>()])!;
        AssertFalse(published);
        AssertFalse(HostRegistries.Data.TryResolve(aggregatePath, out _));
    }
    finally
    {
        HostRegistries.Data.Remove(aggregatePath);
    }
}

static void MonitorRowVisualsHighlightActiveSeverity()
{
    var activeSourcePath = "studio.editor_tests.monitor.visuals.active";
    var inactiveSourcePath = "studio.editor_tests.monitor.visuals.inactive";

    HostRegistries.Data.UpsertSnapshot(activeSourcePath, ItemExtension.CreateWithPath(activeSourcePath, 1d));
    HostRegistries.Data.UpsertSnapshot(inactiveSourcePath, ItemExtension.CreateWithPath(inactiveSourcePath, 10d));

    try
    {
        var ownerItem = new FolderItemModel
        {
            Name = "monitor_widget",
            BodyBackColor = "#FFFFFF",
            BodyBorderColor = "#D4D4D8"
        };
        ownerItem.ApplyTheme(isDarkTheme: false);

        using var activeRow = new MonitorRuleRow(
            ownerItem,
            new MonitorDefinition
            {
                Name = "warning_active",
                SourcePath = activeSourcePath,
                Mode = MonitorRuleMode.Default,
                LowerLimit = "5",
                LogLevel = MonitorLogLevel.Warning
            },
            static () => { });

        using var inactiveRow = new MonitorRuleRow(
            ownerItem,
            new MonitorDefinition
            {
                Name = "warning_inactive",
                SourcePath = inactiveSourcePath,
                Mode = MonitorRuleMode.Default,
                LowerLimit = "5",
                LogLevel = MonitorLogLevel.Warning
            },
            static () => { });

        AssertTrue(activeRow.IsActive);
        AssertFalse(inactiveRow.IsActive);
        AssertEqual(ownerItem.EffectiveBodyBackground, inactiveRow.RowBackground);
        AssertEqual(ownerItem.EffectiveBodyBorder, inactiveRow.RowBorderBrush);
        AssertEqual(ThemePalette.Light.LogWarningForeground, activeRow.RowBorderBrush);
        AssertFalse(string.Equals(ownerItem.EffectiveBodyBackground, activeRow.RowBackground, StringComparison.OrdinalIgnoreCase));
        AssertTrue(Color.TryParse(activeRow.RowBackground, out _));
    }
    finally
    {
        HostRegistries.Data.Remove(activeSourcePath);
        HostRegistries.Data.Remove(inactiveSourcePath);
    }
}

static void MonitorEditorAutoAssignsNextFreeEventId()
{
    var ownerItem = new FolderItemModel
    {
        MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(
        [
            new MonitorDefinition { Name = "monitor_rule_1", EventId = 1 },
            new MonitorDefinition { Name = "monitor_rule_2", EventId = 3 },
            new MonitorDefinition { Name = "monitor_rule_3", EventId = 4 }
        ])
    };

    var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null, targetLogOptions: []);

    AssertEqual("2", viewModel.EventIdText);
}

static void MonitorEditorRejectsDuplicateEventId()
{
    var ownerItem = new FolderItemModel
    {
        MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(
        [
            new MonitorDefinition { Name = "monitor_rule_1", EventId = 7 }
        ])
    };

    var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null, targetLogOptions: [])
    {
        SourcePath = "Logs.source",
        EventIdText = "7"
    };

    AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
    AssertTrue(errorMessage.Contains("unique", StringComparison.Ordinal));
}

static void MonitorEditorRejectsDuplicateEventIdFromCentralFileWithDuplicateNames()
{
    var folderDirectory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(folderDirectory, "Folder.yaml"), "Caption: 'main'");
        new MonitorDefinitionFileCodec().SaveDefinitions(
            folderDirectory: folderDirectory,
            folderName: "main",
            definitions:
            [
                new MonitorDefinition { Name = "monitor_rule_1", SourcePath = "Logs.source", EventId = 7 },
                new MonitorDefinition { Name = "monitor_rule_1", SourcePath = "Logs.source", EventId = 8 }
            ]);

        var ownerItem = new FolderItemModel();
        ownerItem.SetHierarchy("main", parentItem: null);
        ownerItem.SetLayoutFilePath(Path.Combine(folderDirectory, "Folder.yaml"));

        var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null, targetLogOptions: [])
        {
            SourcePath = "Logs.source",
            EventIdText = "7"
        };

        AssertEqual("monitor_rule_2", viewModel.Name);
        AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
        AssertTrue(errorMessage.Contains("Event Id", StringComparison.Ordinal));
        AssertTrue(errorMessage.Contains("unique", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void MonitorEditorRejectsBlankAndZeroEventId()
{
    var ownerItem = new FolderItemModel();

    var blankViewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null, targetLogOptions: [])
    {
        SourcePath = "Logs.source",
        EventIdText = "   "
    };

    AssertFalse(blankViewModel.TryBuildDefinition(out _, out var blankErrorMessage));
    AssertTrue(blankErrorMessage.Contains("required", StringComparison.Ordinal));

    var zeroViewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null, targetLogOptions: [])
    {
        SourcePath = "Logs.source",
        EventIdText = "0"
    };

    AssertFalse(zeroViewModel.TryBuildDefinition(out _, out var zeroErrorMessage));
    AssertTrue(zeroErrorMessage.Contains("greater than 0", StringComparison.Ordinal));
}

static void MonitorEditorAllowsUnchangedEventIdWhenEditing()
{
    var existingDefinition = new MonitorDefinition
    {
        Name = "monitor_rule_1",
        SourcePath = "Logs.source",
        EventId = 7,
        LogLevel = MonitorLogLevel.Warning
    };

    var ownerItem = new FolderItemModel
    {
        MonitorDefinitions = MonitorDefinitionCodec.SerializeDefinitions(
        [
            existingDefinition,
            new MonitorDefinition { Name = "monitor_rule_2", EventId = 8 }
        ])
    };

    var viewModel = new MonitorEditorDialogViewModel(mainWindowViewModel: null, ownerItem, existingDefinition, targetLogOptions: [])
    {
        SourcePath = "Logs.source",
        EventIdText = "7"
    };

    AssertTrue(viewModel.TryBuildDefinition(out var definition, out var errorMessage));
    AssertEqual(string.Empty, errorMessage);
    AssertEqual(7, definition.EventId);
}

static void MonitorWriteLogResolvesRelativeOwnedLogPath()
{
    var sourcePath = "studio.editor_tests.monitor.write_log_source";
    var logName = $"monitor_log_{Guid.NewGuid():N}";
    var ownedLogPath = $"studio.monitor_page.logs.{logName}";
    var relativeLogPath = $"logs.{logName}";

    var source = ItemExtension.CreateWithPath(sourcePath, 1d);
    HostRegistries.Data.UpsertSnapshot(sourcePath, source);
    HornetStudio.Logging.ProcessLogRuntime.EnsurePublished(ownedLogPath, "Monitor Log");

    var ownerItem = new FolderItemModel
    {
        Name = "monitor_widget"
    };
    ownerItem.SetHierarchy("monitor_page", parentItem: null);

    using var row = new MonitorRuleRow(
        ownerItem,
        new MonitorDefinition
        {
            Name = "relative_log_write",
            SourcePath = sourcePath,
            Mode = MonitorRuleMode.Default,
            LowerLimit = "2",
            EventId = 1001,
            EventText = "Relative log write",
            LogLevel = MonitorLogLevel.Warning,
            Actions =
            [
                new MonitorActionDefinition
                {
                    Trigger = MonitorActionTrigger.OnActivated,
                    ActionType = MonitorActionType.WriteLog,
                    TargetLog = relativeLogPath
                }
            ]
        },
        static () => { });

    AssertTrue(HostRegistries.Data.TryResolve(ownedLogPath, out var logItem));
    AssertTrue(logItem?.Value is HornetStudio.Logging.ProcessLog);

    var processLog = (HornetStudio.Logging.ProcessLog)logItem!.Value!;
    var warningEntry = processLog.GetEntries(levelFilter: "Warning").LastOrDefault();
    AssertEqual("Warning", warningEntry?.Level);
    AssertEqual("[1001] Relative log write", warningEntry?.Message);
}

static void EnhancedSignalEditorDefaultsToSnakeCaseName()
{
    var ownerItem = new FolderItemModel
    {
        EnhancedSignalDefinitions = ExtendedSignalDefinitionCodec.SerializeDefinitions(
        [
            new ExtendedSignalDefinition
            {
                Name = "enhanced_signal_1",
                SourcePath = "studio.default_layout.signal_1"
            }
        ])
    };

    var viewModel = new EnhancedSignalEditorDialogViewModel(mainWindowViewModel: null, ownerItem, definition: null);

    AssertEqual("enhanced_signal_2", viewModel.Name);
}

static void EnhancedSignalEditorRejectsUppercaseName()
{
    var viewModel = new EnhancedSignalEditorDialogViewModel(mainWindowViewModel: null, new FolderItemModel(), definition: null)
    {
        Name = "EnhancedSignal1",
        SourcePath = "studio.default_layout.signal_1"
    };

    AssertFalse(viewModel.TryBuildDefinition(out _, out var errorMessage));
    AssertTrue(errorMessage.Contains("snake_case", StringComparison.Ordinal));
}

static void EnhancedSignalRuntimePathUsesSnakeCaseSegments()
{
    var definition = new ExtendedSignalDefinition
    {
        Name = "enhanced_signal_1",
        SourcePath = "studio.default_layout.signal_1"
    };

    AssertEqual(
        "studio.default_layout.enhanced_signals.enhanced_signal_1",
        EnhancedSignalRuntime.BuildRegistryPath("default_layout", definition));
}

static void PythonApplicationRuntimePathUsesSnakeCaseSegments()
{
    var method = typeof(FolderItemModel).GetMethod("GetScriptRuntimePath", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("GetScriptRuntimePath was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.PythonClient,
        Name = "RawApp42",
        PythonScriptPath = "Applications/Python/RawApp42/main.py"
    };
    SetFolderName(item, "Main Folder");

    AssertEqual("studio.main_folder.applications.python.raw_app42", method.Invoke(item, null));
}

static void ApplicationExplorerRegistryRootUsesSnakeCaseSegments()
{
    var method = typeof(ApplicationEntryRow).GetMethod("BuildValueRegistryRootPath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildValueRegistryRootPath was not found.");
    }

    var ownerItem = new FolderItemModel
    {
    };
    SetFolderName(ownerItem, "Main Folder");

    AssertEqual(
        "studio.main_folder.applications.python.raw_test_env",
        method.Invoke(null, [ownerItem, "RawTestEnv"]));
}

static void CircleDisplayRuntimePathUsesSnakeCaseSegments()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.CircleDisplay,
        Name = "CircleDisplay",
        SignalColor = "#22C55E",
        SignalRun = true,
        ProgressBar = true,
        ProgressState = 74,
        ProgressBarColor = "#0EA5E9"
    };

    item.SetHierarchy(pageName: "Main Folder", parentItem: null);

    var basePath = item.GetDisplayRuntimeBasePath();
    var signalColorPath = item.GetDisplayRuntimePath("SignalColor");
    var progressBarColorPath = item.GetDisplayRuntimePath("ProgressBarColor");

    try
    {
        AssertEqual("studio.main_folder.display_runtime.circle_display", basePath);
        AssertEqual("studio.main_folder.display_runtime.circle_display.signal_color", signalColorPath);
        AssertEqual("studio.main_folder.display_runtime.circle_display.progress_bar_color", progressBarColorPath);
        AssertTrue(HostRegistries.Data.TryResolve(basePath, out var runtimeRoot));
        AssertTrue(runtimeRoot?.GetDictionary().ContainsKey("signal_color") == true);
        AssertFalse(runtimeRoot?.GetDictionary().ContainsKey("SignalColor") == true);
        AssertTrue(HostRegistries.Data.TryResolve(signalColorPath, out var signalColor));
        AssertEqual("#22C55E", signalColor?.Value);
        AssertTrue(HostRegistries.Data.TryResolve(progressBarColorPath, out var progressBarColor));
        AssertEqual("#0EA5E9", progressBarColor?.Value);
    }
    finally
    {
        HostRegistries.Data.Remove(signalColorPath);
        HostRegistries.Data.Remove(item.GetDisplayRuntimePath("SignalRun"));
        HostRegistries.Data.Remove(item.GetDisplayRuntimePath("ProgressBar"));
        HostRegistries.Data.Remove(item.GetDisplayRuntimePath("ProgressState"));
        HostRegistries.Data.Remove(progressBarColorPath);
        HostRegistries.Data.Remove(basePath);
    }
}

static void CsvLoggerRuntimePathUsesSnakeCaseSegments()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.CsvLoggerControl,
        Name = "CsvLogger"
    };

    item.SetHierarchy(pageName: "Main Folder", parentItem: null);

    AssertEqual("studio.main_folder.logger_runtime.csv_logger", item.GetLoggerRuntimeBasePath());
    AssertEqual("studio.main_folder.logger_runtime.csv_logger.record", item.GetLoggerRuntimePath("record"));
    AssertEqual("studio.main_folder.logger_runtime.csv_logger.output_path", item.GetLoggerRuntimePath("OutputPath"));
    AssertEqual("studio.main_folder.Loggerruntime.csv_logger.output_path", item.GetLoggerLegacyRuntimePath("OutputPath"));
}

static void SqlLoggerRuntimePathUsesSnakeCaseSegments()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.SqlLoggerControl,
        Name = "SqlLogger"
    };

    item.SetHierarchy(pageName: "Main Folder", parentItem: null);

    AssertEqual("studio.main_folder.logger_runtime.sql_logger", item.GetLoggerRuntimeBasePath());
    AssertEqual("studio.main_folder.logger_runtime.sql_logger.is_recording", item.GetLoggerRuntimePath("IsRecording"));
    AssertEqual("studio.main_folder.logger_runtime.sql_logger.last_file", item.GetLoggerRuntimePath("LastFile"));
    AssertEqual("studio.main_folder.Loggerruntime.sql_logger.last_file", item.GetLoggerLegacyRuntimePath("LastFile"));
}

static void LogControlOwnedPathUsesFolderIdentity()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.LogControl,
        Name = "log_widget"
    };

    item.SetHierarchy("main_page", parentItem: null);

    AssertEqual("studio.main_page.logs.log_widget", item.GetOwnedProcessLogPath());
    AssertEqual("studio.main_page.logs.log_widget", item.GetAutoCreatedLogPath());
}

static void LogControlOwnedDirectoryUsesProjectLogsFolder()
{
    var projectRoot = Path.Combine(Path.GetTempPath(), "HornetStudioEditorTests", Guid.NewGuid().ToString("N"));
    var item = new FolderItemModel
    {
        Kind = ControlKind.LogControl,
        Name = "LogWidget"
    };

    item.SetHierarchy("Main Folder", parentItem: null);

    AssertEqual(Path.Combine(projectRoot, "Logs", "main_folder", "log_widget"), item.GetOwnedProcessLogDirectory(projectRoot));
}

static void LogControlLegacyValuesDoNotControlOwnedPath()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.LogControl,
        Name = "legacy_log_widget",
        TargetLog = "Logs.legacy_target",
        AutoCreateLog = false
    };

    item.SetHierarchy("legacy_page", parentItem: null);

    AssertEqual("studio.legacy_page.logs.legacy_log_widget", item.GetOwnedProcessLogPath());
    AssertEqual("studio.legacy_page.logs.legacy_log_widget", item.GetAutoCreatedLogPath());
}

static void LogControlYamlOmitsLegacyLogProperties()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.LogControl,
        Name = "log_widget"
    };

    item.SetHierarchy("main_page", parentItem: null);

    var method = typeof(MainWindowViewModel).GetMethod("BuildYamlControlDefinition", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildYamlControlDefinition was not found.");
    }

    var definition = (JsonObject?)method.Invoke(null, [item]);
    AssertTrue(definition is not null);
    var properties = definition!["Properties"] as JsonObject;
    if (properties is null)
    {
        AssertFalse(definition.ContainsKey("Properties"));
        return;
    }

    AssertFalse(properties.ContainsKey("TargetLog"));
    AssertFalse(properties.ContainsKey("AutoCreateLog"));
}

static void LogControlDocumentSerializationOmitsLegacyLogProperties()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.LogControl,
        Name = "log_widget",
        TargetLog = "Logs.legacy_target",
        AutoCreateLog = true
    };

    item.SetHierarchy("main_page", parentItem: null);

    var method = typeof(MainWindowViewModel).GetMethod("ToDocument", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(FolderItemModel)], null);
    if (method is null)
    {
        throw new InvalidOperationException("ToDocument was not found.");
    }

    var document = (FolderItemDocument?)method.Invoke(null, [item]);
    AssertTrue(document is not null);

    var serialized = JsonSerializer.Serialize(document);
    AssertFalse(serialized.Contains("TargetLog", StringComparison.Ordinal));
    AssertFalse(serialized.Contains("AutoCreateLog", StringComparison.Ordinal));
}

static void LogControlEnsuresOwnedProcessLogPublication()
{
    var projectRoot = Path.Combine(Path.GetTempPath(), "HornetStudioEditorTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(projectRoot, "Folders"));
    File.WriteAllText(Path.Combine(projectRoot, "Program.cs"), string.Empty);

    var item = new FolderItemModel
    {
        Kind = ControlKind.LogControl,
        Name = $"log_widget_{Guid.NewGuid():N}"
    };

    item.SetHierarchy("runtime_page", parentItem: null);
    Core.SetOpenedDirectory(projectRoot);

    var ownedPath = item.GetOwnedProcessLogPath();
    var ownedDirectory = item.GetOwnedProcessLogDirectory(projectRoot);
    item.EnsureOwnedProcessLog();

    AssertTrue(HostRegistries.Data.TryResolve(ownedPath, out var logItem));
    AssertTrue(logItem?.Value is HornetStudio.Logging.ProcessLog);
    AssertEqual(ownedDirectory, ((HornetStudio.Logging.ProcessLog)logItem!.Value!).LogDirectory);

    foreach (var level in new[] { "debug", "info", "warning", "error", "fatal" })
    {
        AssertTrue(HostRegistries.Data.TryResolve($"{ownedPath}.{level}", out var inputItem));
        AssertEqual(true, inputItem?.Properties["writable"].Value);
    }
}

static void LoggerRuntimeControlConstantsUseSnakeCase()
{
    AssertEqual("record", GetPrivateConstString(typeof(EditorCsvLoggerControl), "RecordItemName"));
    AssertEqual("output_path", GetPrivateConstString(typeof(EditorCsvLoggerControl), "OutputPathItemName"));
    AssertEqual("is_recording", GetPrivateConstString(typeof(EditorCsvLoggerControl), "IsRecordingItemName"));
    AssertEqual("last_file", GetPrivateConstString(typeof(EditorCsvLoggerControl), "LastFileItemName"));
    AssertEqual("status", GetPrivateConstString(typeof(EditorCsvLoggerControl), "StatusItemName"));

    AssertEqual("record", GetPrivateConstString(typeof(EditorSqlLoggerControl), "RecordItemName"));
    AssertEqual("output_path", GetPrivateConstString(typeof(EditorSqlLoggerControl), "OutputPathItemName"));
    AssertEqual("is_recording", GetPrivateConstString(typeof(EditorSqlLoggerControl), "IsRecordingItemName"));
    AssertEqual("last_file", GetPrivateConstString(typeof(EditorSqlLoggerControl), "LastFileItemName"));
    AssertEqual("status", GetPrivateConstString(typeof(EditorSqlLoggerControl), "StatusItemName"));
}

static void SetFolderName(FolderItemModel item, string folderName)
{
    var property = typeof(FolderItemModel).GetProperty("FolderName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    if (property is null)
    {
        throw new InvalidOperationException("FolderName property was not found.");
    }

    property.SetValue(item, folderName);
}

static void ItemExposureCodecRoundtrip()
{
    var serialized = ItemExposureDefinitionCodec.SerializeDefinitions(
    [
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.mask",
            Format = "b4",
            Unit = "flags",
            ExposeBits = true,
            BitCount = 4,
            BitLabels = "Bit0=Ready"
        }
    ]);

    var parsed = ItemExposureDefinitionCodec.ParseDefinitions(serialized);
    AssertEqual(1, parsed.Count);
    AssertEqual("broker1.client1.device.mask", parsed[0].ItemPath);
    AssertEqual("b4", parsed[0].Format);
    AssertEqual("flags", parsed[0].Unit);
    AssertEqual(true, parsed[0].ExposeBits);
    AssertEqual(4, parsed[0].BitCount);
    AssertEqual("Bit0=Ready", parsed[0].BitLabels);
}

static void TargetPropertyOptionsHideProtectedProperties()
{
    var item = ItemExtension.CreateWithPath("runtime.policy_picker.device");
    item.Properties["read"].Value = 1;
    item.Properties["unit"].Value = "V";
    item.Properties["write"].Value = 1;
    item.Properties["writable"].Value = true;
    item.Properties["write_path"].Value = "runtime.policy_picker.device.request";
    HostRegistries.Data.UpsertSnapshot("runtime.policy_picker.device", item);

    var method = typeof(MainWindowViewModel).GetMethod("GetTargetPropertyOptions", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("GetTargetPropertyOptions was not found.");
    }

    var options = ((IEnumerable<string>)method.Invoke(null, ["runtime.policy_picker.device", string.Empty])!)
        .ToArray();

    AssertTrue(options.Contains("read", StringComparer.OrdinalIgnoreCase));
    AssertEqual("read", options[0]);
    AssertTrue(options.Contains("Unit", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("write", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("Writable", StringComparer.OrdinalIgnoreCase));
    AssertFalse(options.Contains("WritePath", StringComparer.OrdinalIgnoreCase));
}

static void TargetPropertyFieldHiddenForNormalWidgets()
{
    var method = typeof(MainWindowViewModel).GetMethod("ShouldShowEditorDialogField", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ShouldShowEditorDialogField was not found.");
    }

    AssertFalse((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemModel }, "TargetPropertyPath"])!);
    AssertFalse((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.Signal }, "TargetPropertyPath"])!);
    AssertTrue((bool)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemModel }, "TargetPath"])!);
}

static void TargetPropertyDefaultsToValue()
{
    var target = ItemExtension.CreateWithPath("runtime.policy_picker.default_device");
    target.Properties["read"].Value = 42;
    target.Properties["unit"].Value = "V";
    HostRegistries.Data.UpsertSnapshot("runtime.policy_picker.default_device", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "runtime.policy_picker.default_device";

    AssertEqual("read", item.TargetPropertyPath);
}

static void TargetPropertyProtectedFallbackUsesValue()
{
    var target = ItemExtension.CreateWithPath("runtime.policy_picker.protected_device");
    target.Properties["read"].Value = 7;
    target.Properties["write_path"].Value = "runtime.policy_picker.protected_device.request";
    HostRegistries.Data.UpsertSnapshot("runtime.policy_picker.protected_device", target);

    var item = new FolderItemModel { Kind = ControlKind.Signal };
    item.TargetPath = "runtime.policy_picker.protected_device";
    item.TargetPropertyPath = "WritePath";

    AssertEqual("read", item.TargetPropertyPath);
    AssertEqual("read", item.TargetPropertyView.Property?.Name);
}

static void ItemClientModeDefaultsToExternal()
{
    var item = new FolderItemModel { Kind = ControlKind.ItemClient };

    AssertEqual(ItemClientModes.External, item.BrokerMode);
}

static void ItemClientModeNormalizesValues()
{
    var item = new FolderItemModel { Kind = ControlKind.ItemClient };

    item.BrokerMode = "Own";
    AssertEqual(ItemClientModes.Own, item.BrokerMode);

    item.BrokerMode = "unexpected";
    AssertEqual(ItemClientModes.External, item.BrokerMode);
}

static void ItemClientBaseTopicAllowsEmpty()
{
    var item = new FolderItemModel { Kind = ControlKind.ItemClient };

    AssertEqual(string.Empty, item.BrokerBaseTopic);

    item.BrokerBaseTopic = " edm1 ";
    AssertEqual("edm1", item.BrokerBaseTopic);

    item.BrokerBaseTopic = " ";
    AssertEqual(string.Empty, item.BrokerBaseTopic);
}

static void ItemClientLayoutDocumentDefaultsToExternalMode()
{
    var document = new FolderItemDocument();

    AssertEqual(ItemClientModes.External, document.BrokerMode);
}

static void ItemClientPublishItemsDefaultEmpty()
{
    var item = new FolderItemModel { Kind = ControlKind.ItemClient };

    AssertEqual(string.Empty, item.BrokerPublishedItemPaths);
}

static void ItemClientLayoutPublishItemsDefaultEmpty()
{
    var document = new FolderItemDocument();

    AssertEqual(string.Empty, document.BrokerPublishedItemPaths);
}

static void ItemClientPublishOptionsUseMetadata()
{
    var publicPath = "studio.metadata_publish.public_signal";
    var customSignalPath = "studio.metadata_publish.custom_signals.signal1";
    var enhancedSignalPath = "studio.metadata_publish.enhanced_signals.signal1";
    var internalPath = "studio.metadata_publish.broker.status.attach_options";
    HostRegistries.Data.Remove(publicPath);
    HostRegistries.Data.Remove(customSignalPath);
    HostRegistries.Data.Remove(enhancedSignalPath);
    HostRegistries.Data.Remove(internalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(publicPath, ItemExtension.CreateWithPath(publicPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(customSignalPath, ItemExtension.CreateWithPath(customSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(enhancedSignalPath, ItemExtension.CreateWithPath(enhancedSignalPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(internalPath, ItemExtension.CreateWithPath(internalPath), DataRegistryItemMetadata.WidgetInternal());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemClient }])!).ToArray();

        AssertTrue(options.Contains(publicPath, StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains(customSignalPath, StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains(enhancedSignalPath, StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains(internalPath, StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(publicPath);
        HostRegistries.Data.Remove(customSignalPath);
        HostRegistries.Data.Remove(enhancedSignalPath);
        HostRegistries.Data.Remove(internalPath);
    }
}

static void ItemClientPublishOptionsExcludeBrokerReceivedItems()
{
    var localPath = "studio.metadata_publish.local_signal";
    var receivedPath = "studio.metadata_publish.item_client1.device.temperature";
    HostRegistries.Data.Remove(localPath);
    HostRegistries.Data.Remove(receivedPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(receivedPath, ItemExtension.CreateWithPath(receivedPath), DataRegistryItemMetadata.BrokerReceivedData());

        AssertTrue(HostRegistries.Data.TryGetMetadata(receivedPath, out var metadata));
        AssertFalse(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerPublish));
        AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.Display));
        AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.BrokerAttach));
        AssertTrue(metadata.Capabilities.HasFlag(DataRegistryItemCapabilities.DebugInspect));

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemClient }])!).ToArray();

        AssertTrue(options.Contains(localPath, StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains(receivedPath, StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
        HostRegistries.Data.Remove(receivedPath);
    }
}

static void ItemClientPublishOptionsDeduplicateLegacyRoots()
{
    var legacyPath = "project.metadata_publish.deduplicated_signal";
    var canonicalPath = "studio.metadata_publish.deduplicated_signal";
    HostRegistries.Data.Remove(legacyPath);
    HostRegistries.Data.Remove(canonicalPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(legacyPath, ItemExtension.CreateWithPath(legacyPath), DataRegistryItemMetadata.PublicData());
        HostRegistries.Data.UpsertSnapshot(canonicalPath, ItemExtension.CreateWithPath(canonicalPath), DataRegistryItemMetadata.PublicData());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerPublishItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerPublishItemOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [new FolderItemModel { Kind = ControlKind.ItemClient }])!)
            .Where(path => path.EndsWith(".metadata_publish.deduplicated_signal", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        AssertEqual(1, options.Length);
        AssertEqual(canonicalPath, options[0]);
    }
    finally
    {
        HostRegistries.Data.Remove(legacyPath);
        HostRegistries.Data.Remove(canonicalPath);
    }
}

static void ItemTreeVisibilityUsesDisplayMetadata()
{
    var internalPath = "studio.registry_visibility.item_client1.status.attach_options.item_client1.edm1.temperature";
    var receivedPath = "studio.registry_visibility.item_client1.edm1.temperature";
    HostRegistries.Data.Remove(internalPath);
    HostRegistries.Data.Remove(receivedPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(internalPath, ItemExtension.CreateWithPath(internalPath), DataRegistryItemMetadata.WidgetInternal());
        HostRegistries.Data.UpsertSnapshot(receivedPath, ItemExtension.CreateWithPath(receivedPath, 21.5), DataRegistryItemMetadata.BrokerReceivedData());

        using var viewModel = new HornetStudio.ViewModels.ItemTreeWindowViewModel();
        var refreshMethod = typeof(HornetStudio.ViewModels.ItemTreeWindowViewModel).GetMethod("RefreshTree", BindingFlags.NonPublic | BindingFlags.Instance);
        if (refreshMethod is null)
        {
            throw new InvalidOperationException("RefreshTree was not found.");
        }

        refreshMethod.Invoke(viewModel, []);

        AssertTrue(ContainsTreePath(viewModel.RootNodes, receivedPath));
        AssertFalse(ContainsTreePath(viewModel.RootNodes, internalPath));
    }
    finally
    {
        HostRegistries.Data.Remove(internalPath);
        HostRegistries.Data.Remove(receivedPath);
    }
}

static void BrokerAttachOptionsUseInternalDiscovery()
{
    var attachOptionPath = "studio.registry_visibility.item_client1.status.attach_options.item_client1.edm1.temperature";
    HostRegistries.Data.Remove(attachOptionPath);

    try
    {
        HostRegistries.Data.UpsertSnapshot(attachOptionPath, ItemExtension.CreateWithPath(attachOptionPath), DataRegistryItemMetadata.WidgetInternal());

        var method = typeof(MainWindowViewModel).GetMethod("GetBrokerAttachItemOptions", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("GetBrokerAttachItemOptions was not found.");
        }

        var item = new FolderItemModel
        {
            Kind = ControlKind.ItemClient,
            Name = "ItemClient1"
        };
        item.SetHierarchy("RegistryVisibility", parentItem: null);

        var options = ((IEnumerable<string>)method.Invoke(null, [item])!).ToArray();

        AssertTrue(options.Contains("item_client1.edm1.temperature", StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(attachOptionPath);
    }
}

static void ItemClientPublishItemsRendersFlatAttachRows()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.ItemClient,
        BrokerPublishedItemPaths = "studio.default_layout.Edm1.Pressure"
    };
    var definition = new EditorDialogBindingDefinition(
        "BrokerPublishedItemPaths",
        "PublishItems",
        EditorPropertyType.AttachItemList,
        current => current.BrokerPublishedItemPaths,
        optionsFactory: _ =>
        [
            "studio.default_layout.Edm1.Pressure",
            "studio.default_layout.Edm1.Temperature"
        ]);

    var field = definition.CreateField(item);

    AssertEqual(2, field.AttachItemEntries.Count);
    AssertTrue(field.AttachItemEntries.All(static row => !row.IsGroup));
    AssertEqual("pressure", field.AttachItemEntries[0].DisplayName);
    AssertEqual("studio.default_layout.edm1", field.AttachItemEntries[0].DisplaySource);
    AssertEqual(true, field.AttachItemEntries[0].IsAttached);
    AssertEqual(false, field.AttachItemEntries[1].IsAttached);
}

static void ItemClientAttachedBodyRowHidesWidgetPrefix()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.ItemClient,
        Name = "item_client_1"
    };

    var row = new BrokerAttachedItemRow(
        ownerItem: item,
        itemPath: "item_client_1.edm1.pressure",
        displayName: "pressure",
        summaryText: "Live broker item.",
        alertText: string.Empty,
        isLive: true);

    AssertEqual("item_client_1.edm1.pressure", row.ItemPath);
    AssertEqual("edm1.pressure", row.PathText);
}

static void ItemClientPublishedBodyRowHidesStudioFolderPrefix()
{
    var item = new FolderItemModel
    {
        Kind = ControlKind.ItemClient,
        Name = "item_client_1"
    };
    item.SetHierarchy("main", parentItem: null);

    var row = new BrokerPublishedRootRow(
        ownerItem: item,
        localRootPath: "studio.main.enhanced_signals.filtered_1",
        displayName: "filtered_1",
        summaryText: "1 active entry.",
        alertText: string.Empty,
        hasActiveEntries: true,
        exists: true);

    AssertEqual("studio.main.enhanced_signals.filtered_1", row.LocalRootPath);
    AssertEqual("enhanced_signals.filtered_1", row.PathText);
}

static void ItemClientPublishedDialogShowsRootRowAndHidesFolderPrefix()
{
    var rootPath = "studio.main.enhanced_signals.filtered_1";
    var childPath = "studio.main.enhanced_signals.filtered_1.command";
    var definitions = new BrokerPublishedItemDefinition[]
    {
        new()
        {
            LocalRootPath = rootPath,
            LocalPath = rootPath,
            BrokerPath = rootPath,
            Active = true
        },
        new()
        {
            LocalRootPath = rootPath,
            LocalPath = childPath,
            BrokerPath = childPath,
            Active = true
        }
    };

    var viewModelType = typeof(PublishedItemDialogWindow).GetNestedType("DialogViewModel", BindingFlags.NonPublic);
    var method = viewModelType?.GetMethod("BuildRows", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("PublishedItemDialogWindow.DialogViewModel.BuildRows was not found.");
    }

    var rows = ((IEnumerable<PublishedItemEditorRow>)method.Invoke(null, [definitions, rootPath])!).ToArray();

    AssertEqual(2, rows.Length);
    AssertEqual(rootPath, rows[0].LocalPath);
    AssertEqual("enhanced_signals.filtered_1", rows[0].DisplayName);
    AssertEqual(childPath, rows[1].LocalPath);
    AssertEqual("enhanced_signals.filtered_1.command", rows[1].DisplayName);
}

static void ItemClientReceivedPathUsesFlatWidgetBranch()
{
    var method = typeof(ItemClientControl).GetMethod("BuildReceivedMqttRuntimePath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildReceivedMqttRuntimePath was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.ItemClient,
        Name = "ItemClient1"
    };
    item.SetHierarchy("default_layout", parentItem: null);

    var path = (string)method.Invoke(null, [item, "shared", "Edm1.Pressure"])!;

    AssertEqual("studio.default_layout.item_client1.edm1.pressure", path);
    AssertFalse(path.Contains("shared", StringComparison.OrdinalIgnoreCase));
    AssertFalse(path.Contains("Status.AttachOptions", StringComparison.OrdinalIgnoreCase));
}

static void ItemClientReceivedPathCollapsesNestedMqttIdentity()
{
    var method = typeof(ItemClientControl).GetMethod("BuildReceivedMqttRuntimePath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildReceivedMqttRuntimePath was not found.");
    }

    var item = new FolderItemModel
    {
        Kind = ControlKind.ItemClient,
        Name = "ItemClient1"
    };
    item.SetHierarchy("default_layout", parentItem: null);

    var path = (string)method.Invoke(null, [item, "Edm1.Pressure", "item_client1.edm1.pressure"])!;

    AssertEqual("studio.default_layout.item_client1.edm1.pressure", path);
    AssertFalse(path.Contains("Edm1.Pressure.ItemClient1", StringComparison.OrdinalIgnoreCase));
}

static void ItemClientAttachIdentityCollapsesNestedMqttIdentity()
{
    var method = typeof(ItemClientControl).GetMethod("BuildBrokerAttachIdentity", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildBrokerAttachIdentity was not found.");
    }

    var path = (string)method.Invoke(null, ["item_client1", string.Empty, "Edm1.Pressure", "item_client1.edm1.pressure"])!;

    AssertEqual("item_client1.edm1.pressure", path);
    AssertFalse(path.Contains("Edm1.Pressure.ItemClient1", StringComparison.OrdinalIgnoreCase));
}

static void ItemClientAttachIdentityIncludesBaseTopic()
{
    var method = typeof(ItemClientControl).GetMethod("BuildBrokerAttachIdentity", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildBrokerAttachIdentity was not found.");
    }

    var path = (string)method.Invoke(null, ["item_client1", "edm1", "shared", "pressure"])!;

    AssertEqual("item_client1.edm1.pressure", path);
}

static void ItemClientAttachOptionsUseItemValues()
{
    var method = typeof(ItemClientControl).GetMethod(
        "EnumerateAttachOptions",
        BindingFlags.Static | BindingFlags.NonPublic,
        types: [typeof(string), typeof(string), typeof(IReadOnlyDictionary<string, ItemModel>)]);
    if (method is null)
    {
        throw new InvalidOperationException("Broker attach option helper was not found.");
    }

    var root = new ItemModel("shared", path: "runtime.item_broker.item_client1");
    root["edm1"] = new ItemModel("edm1", path: root.Path);
    root["edm1"]["pressure"] = ItemExtension.CreateWithPath("runtime.item_broker.item_client1.shared.edm1.pressure", 12.5f);

    var snapshots = new Dictionary<string, ItemModel>(StringComparer.OrdinalIgnoreCase)
    {
        ["shared"] = root,
    };

    var options = ((IEnumerable<string>)method.Invoke(null, ["item_client1", string.Empty, snapshots])!).ToArray();

    AssertTrue(options.Contains("item_client1.edm1.pressure", StringComparer.OrdinalIgnoreCase));
}

static void ItemClientAttachOptionPathSplitsDottedIdentity()
{
    var method = typeof(ItemClientControl).GetMethod("BuildAttachOptionPath", BindingFlags.Static | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException("BuildAttachOptionPath was not found.");
    }

    var path = (string)method.Invoke(null, ["studio.registry_visibility.item_client1.status.attach_options", "item_client1.pressure"])!;

    AssertEqual("studio.registry_visibility.item_client1.status.attach_options.item_client1.pressure", path);
    _ = ItemExtension.CreateWithPath(path);
}

static void BrokerAttachNormalizationStripsPrefixBeforeMqttIdentity()
{
    var helperType = typeof(MainWindowViewModel).Assembly.GetType("HornetStudio.Editor.Helpers.TargetPathHelper");
    if (helperType is null)
    {
        throw new InvalidOperationException("TargetPathHelper was not found.");
    }

    var method = helperType.GetMethod("ToBrokerReceivedAttachIdentity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ToBrokerReceivedAttachIdentity was not found.");
    }

    AssertEqual("item_client1.edm1.pressure", method.Invoke(null, ["Edm1.Pressure.ItemClient1.Mqtt.Edm1.Pressure"]));
    AssertEqual("item_client1.edm1.pressure", method.Invoke(null, ["studio.Folder1.ItemClient1.Mqtt.Edm1.Pressure"]));
}

static void ItemClientAttachSelectionNormalizesLegacySharedPath()
{
    var item = new FolderItemModel { Kind = ControlKind.ItemClient };
    var definition = new EditorDialogBindingDefinition(
        "BrokerAttachedItemPaths",
        "AttachToUi",
        EditorPropertyType.AttachItemList,
        _ => "runtime.item_broker.ItemClient1.shared.Edm1.Pressure",
        optionsFactory: _ => ["item_client1.edm1.pressure"]);

    var field = definition.CreateField(item);

    AssertEqual(1, field.AttachItemEntries.Count);
    AssertEqual("item_client1.edm1.pressure", field.AttachItemEntries[0].RelativePath);
    AssertEqual(true, field.AttachItemEntries[0].IsAttached);
    AssertEqual(false, field.AttachItemEntries[0].IsMissing);
}

static void UdlAttachAddNormalizesAndDeduplicatesPaths()
{
    var method = typeof(UdlClientControl).GetMethod("AddAttachedPath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("AddAttachedPath was not found.");
    }

    var updated = (string)method.Invoke(null, ["project.default_layout.ModuleA", "studio.default_layout.ModuleA"])!;
    AssertEqual("studio.default_layout.module_a", updated);

    updated = (string)method.Invoke(null, [updated, "studio.default_layout.ModuleA.SubItem"])!;
    AssertEqual("studio.default_layout.module_a", updated);
}

static void UdlAttachRemoveClearsSelectedPath()
{
    var method = typeof(UdlClientControl).GetMethod("RemoveAttachedPath", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("RemoveAttachedPath was not found.");
    }

    var updated = (string)method.Invoke(null, ["studio.default_layout.ModuleA\r\nstudio.default_layout.ModuleB", "project.default_layout.ModuleA"])!;
    AssertEqual("studio.default_layout.module_b", updated);

    updated = (string)method.Invoke(null, [updated, "studio.default_layout.ModuleB"])!;
    AssertEqual(string.Empty, updated);
}

static void UdlReceivedRowsStayVisibleWhenAttached()
{
    var method = typeof(UdlClientControl).GetMethod("BuildReceivedAttachSectionRows", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildReceivedAttachSectionRows was not found.");
    }

    var ownerItem = new FolderItemModel { Kind = ControlKind.UdlClientControl, Name = "udl_client_control" };
    ownerItem.SetHierarchy("default_layout", null);
    var detachedRows = (IReadOnlyList<UdlClientAttachSectionRow>)method.Invoke(null, [ownerItem, new[] { "m310" }, new HashSet<string>(StringComparer.OrdinalIgnoreCase)])!;

    AssertEqual(1, detachedRows.Count);
    AssertEqual("m310", detachedRows[0].RelativePath);
    AssertEqual("Attach", detachedRows[0].ActionText);
    AssertEqual(true, detachedRows[0].CanExecuteAction);

    var attachedRows = (IReadOnlyList<UdlClientAttachSectionRow>)method.Invoke(null, [ownerItem, new[] { "m310" }, new HashSet<string>(["m310"], StringComparer.OrdinalIgnoreCase)])!;

    AssertEqual(1, attachedRows.Count);
    AssertEqual("m310", attachedRows[0].RelativePath);
    AssertEqual("Attached", attachedRows[0].ActionText);
    AssertEqual(false, attachedRows[0].CanExecuteAction);
}

static void UdlAttachedItemsResolveViaRuntimeRegistry()
{
    var method = typeof(UdlClientControl).GetMethod("ResolveRuntimeItemFromSources", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("ResolveRuntimeItemFromSources was not found.");
    }

    var ownerItem = new FolderItemModel { Kind = ControlKind.UdlClientControl, Name = "udl_client_control" };
    ownerItem.SetHierarchy("default_layout", null);

    var runtimePath = "runtime.udl_client.udl_client_control.m310";
    HostRegistries.Data.Remove(runtimePath);

    try
    {
        var runtimeItem = ItemExtension.CreateWithPath(runtimePath);
        HostRegistries.Data.UpsertSnapshot(runtimePath, runtimeItem, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        var resolved = (ItemModel?)method.Invoke(null, [ownerItem, Array.Empty<ItemModel>(), "m310"]);
        AssertTrue(resolved is not null);
        AssertEqual(runtimePath, resolved!.Path);
    }
    finally
    {
        HostRegistries.Data.Remove(runtimePath);
    }
}

static void UdlRuntimeReceivedItemsStayOutOfRegistryUntilAttach()
{
    const string attachedPath = "studio.default_layout.udl_client_control.m001";
    HostRegistries.Data.Remove(attachedPath);

    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0
    };

    using var runtime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "udl_client_control",
        demoEnabled: true,
        definitions: [definition]);
    using var projection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "udl_client_control");

    try
    {
        ConnectUdlRuntime(runtime);

        var receivedRoots = GetUdlRuntimeReceivedRootPaths(runtime);
        AssertTrue(receivedRoots.Contains("m001", StringComparer.OrdinalIgnoreCase));
        AssertFalse(HostRegistries.Data.TryResolve(attachedPath, out _));

        var projectionInput = GetUdlRuntimeAttachmentProjectionInput(runtime, "m001");
        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, projectionInput));
        AssertEqual(false, SynchronizeUdlHostRegistryProjection(projection, projectionInput));
        AssertTrue(HostRegistries.Data.TryResolve(attachedPath, out var attached));
        AssertEqual(attachedPath, attached?.Path);
        AssertFalse(attachedPath.Contains("demo", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlReadAttachmentPublishesValueReferenceMetadata()
{
    const string attachedReadPath = "studio.default_layout.udl_client_control.m001.read";
    HostRegistries.Data.Remove(attachedReadPath);

    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0
    };

    using var runtime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "udl_client_control",
        demoEnabled: true,
        definitions: [definition]);
    using var projection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "udl_client_control");

    try
    {
        ConnectUdlRuntime(runtime);

        var projectionInput = GetUdlRuntimeAttachmentProjectionInput(runtime, "m001");
        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, projectionInput));
        AssertTrue(HostRegistries.Data.TryResolve(attachedReadPath, out var attachedRead));
        AssertEqual("runtime.udl_client.udl_client_control.m001.read", attachedRead?.Properties["valueRefPath"].Value?.ToString());
        AssertEqual("read", attachedRead?.Properties["valueRefParameter"].Value?.ToString());
    }
    finally
    {
        HostRegistries.Data.Remove(attachedReadPath);
        HostRegistries.Data.Remove("studio.default_layout.udl_client_control.m001");
    }
}

static void UdlReadAttachmentKeepsValueReferenceMetadataAfterSnapshotRefresh()
{
    const string attachedReadPath = "studio.default_layout.udl_client_control.m001.read";
    HostRegistries.Data.Remove(attachedReadPath);

    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0
    };

    using var runtime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "udl_client_control",
        demoEnabled: true,
        definitions: [definition]);
    using var projection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "udl_client_control");

    try
    {
        ConnectUdlRuntime(runtime);

        var projectionInput = GetUdlRuntimeAttachmentProjectionInput(runtime, "m001");
        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, projectionInput));

        var runtimeModule = ResolveUdlRuntimeItem(runtime, "m001");
        runtimeModule["read"].Properties["custom_refresh_marker"].Value = 123;

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(attachedReadPath, out var attachedRead)
                && attachedRead is not null
                && attachedRead.Properties.Has("custom_refresh_marker")
                && object.Equals(attachedRead.Properties["custom_refresh_marker"].Value, 123),
            1000));

        AssertTrue(HostRegistries.Data.TryResolve(attachedReadPath, out var refreshedRead));
        AssertEqual("runtime.udl_client.udl_client_control.m001.read", refreshedRead?.Properties["valueRefPath"].Value?.ToString());
        AssertEqual("read", refreshedRead?.Properties["valueRefParameter"].Value?.ToString());
    }
    finally
    {
        HostRegistries.Data.Remove(attachedReadPath);
        HostRegistries.Data.Remove("studio.default_layout.udl_client_control.m001");
    }
}

static void UdlValueReferenceWarmupResyncAddsOnlyNewPublicRoots()
{
    const string firstPath = "studio.default_layout.udl_client_control.m001";
    const string secondPath = "studio.default_layout.udl_client_control.m002";
    HostRegistries.Data.Remove(firstPath);
    HostRegistries.Data.Remove(secondPath);

    var definitions = new[]
    {
        new UdlDemoModuleDefinition
        {
            Name = "m001",
            Kind = UdlDemoModuleKind.SetDriven,
            InitialValue = 0
        },
        new UdlDemoModuleDefinition
        {
            Name = "m002",
            Kind = UdlDemoModuleKind.SetDriven,
            InitialValue = 0
        }
    };

    using var runtime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "udl_client_control",
        demoEnabled: true,
        definitions: definitions);
    using var projection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "udl_client_control");

    var addedRoots = new List<string>();
    void OnRegistryChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind != DataChangeKind.SnapshotUpserted)
        {
            return;
        }

        if (string.Equals(e.Key, firstPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Key, secondPath, StringComparison.OrdinalIgnoreCase))
        {
            addedRoots.Add(e.Key);
        }
    }

    HostRegistries.Data.RegistryChanged += OnRegistryChanged;

    try
    {
        ConnectUdlRuntime(runtime);

        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, GetUdlRuntimeAttachmentProjectionInput(runtime, "m001")));
        AssertEqual(1, addedRoots.Count);
        AssertEqual(firstPath, addedRoots[0]);

        addedRoots.Clear();
        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, GetUdlRuntimeAttachmentProjectionInput(runtime, "m001\nm002")));
        AssertEqual(1, addedRoots.Count);
        AssertEqual(secondPath, addedRoots[0]);

        addedRoots.Clear();
        AssertEqual(false, SynchronizeUdlHostRegistryProjection(projection, GetUdlRuntimeAttachmentProjectionInput(runtime, "m001\nm002")));
        AssertEqual(0, addedRoots.Count);
    }
    finally
    {
        HostRegistries.Data.RegistryChanged -= OnRegistryChanged;
        HostRegistries.Data.Remove(firstPath);
        HostRegistries.Data.Remove(secondPath);
    }
}

static void UdlProjectionClearsDetachedPublicItems()
{
    const string attachedPath = "studio.default_layout.udl_client_control.m001";
    HostRegistries.Data.Remove(attachedPath);

    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0
    };

    using var runtime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "udl_client_control",
        demoEnabled: true,
        definitions: [definition]);
    using var projection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "udl_client_control");

    try
    {
        ConnectUdlRuntime(runtime);

        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, GetUdlRuntimeAttachmentProjectionInput(runtime, "m001")));
        AssertTrue(HostRegistries.Data.TryResolve(attachedPath, out _));

        AssertEqual(true, SynchronizeUdlHostRegistryProjection(projection, GetUdlRuntimeAttachmentProjectionInput(runtime, string.Empty)));
        AssertFalse(HostRegistries.Data.TryResolve(attachedPath, out _));
        AssertEqual(false, SynchronizeUdlHostRegistryProjection(projection, GetUdlRuntimeAttachmentProjectionInput(runtime, string.Empty)));
    }
    finally
    {
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeIsolatesAttachedPathsPerClient()
{
    const string firstPath = "studio.default_layout.engine_client.m001";
    const string secondPath = "studio.default_layout.service_client.m001";
    HostRegistries.Data.Remove(firstPath);
    HostRegistries.Data.Remove(secondPath);

    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0
    };

    using var firstRuntime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "engine_client",
        demoEnabled: true,
        definitions: [definition]);
    using var secondRuntime = CreateUdlRuntime(
        folderName: "default_layout",
        clientName: "service_client",
        demoEnabled: true,
        definitions: [definition]);
    using var firstProjection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "engine_client");
    using var secondProjection = CreateUdlHostRegistryProjection(
        folderName: "default_layout",
        clientName: "service_client");

    try
    {
        ConnectUdlRuntime(firstRuntime);
        ConnectUdlRuntime(secondRuntime);

        AssertEqual(true, SynchronizeUdlHostRegistryProjection(firstProjection, GetUdlRuntimeAttachmentProjectionInput(firstRuntime, "m001")));
        AssertEqual(true, SynchronizeUdlHostRegistryProjection(secondProjection, GetUdlRuntimeAttachmentProjectionInput(secondRuntime, "m001")));

        AssertTrue(HostRegistries.Data.TryResolve(firstPath, out var firstAttached));
        AssertTrue(HostRegistries.Data.TryResolve(secondPath, out var secondAttached));
        AssertEqual(firstPath, firstAttached?.Path);
        AssertEqual(secondPath, secondAttached?.Path);
        AssertFalse(string.Equals(firstAttached?.Path, secondAttached?.Path, StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(firstPath);
        HostRegistries.Data.Remove(secondPath);
    }
}

static void UdlSetDrivenDemoWritesFeedbackToReadOnly()
{
    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0,
        SetScale = 1,
        SetOffset = 0,
        SetTauSeconds = 0
    };

    using var client = new SimulatedHostUdlClient(
        name: "udl_client_control",
        host: "demo",
        port: 9001,
        definitions: [definition]);

    client.ConnectAsync().GetAwaiter().GetResult();
    try
    {
        var module = client.Items["m001"];
        module["set"].Properties["write"].Value = 20d;

        Thread.Sleep(250);

        AssertEqual(20d, module["read"].Properties["read"].Value);
        AssertEqual(20d, module["set"].Properties["write"].Value);
        AssertEqual(20d, module["set"].Properties["read"].Value);
    }
    finally
    {
        client.DisconnectAsync().GetAwaiter().GetResult();
    }
}

static void UdlSimulatedDemoPublishesChannelTypeMetadata()
{
    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0
    };

    using var client = new SimulatedHostUdlClient(
        name: "udl_client_control",
        host: "demo",
        port: 9001,
        definitions: [definition]);

    client.ConnectAsync().GetAwaiter().GetResult();
    try
    {
        var module = client.Items["m001"];

        AssertEqual("float", module["read"].Properties["type"].Value);
        AssertEqual("float", module["set"].Properties["type"].Value);
        AssertEqual("float", module["out"].Properties["type"].Value);
        AssertEqual("int", module["state"].Properties["type"].Value);
        AssertEqual("int", module["alert"].Properties["type"].Value);
    }
    finally
    {
        client.DisconnectAsync().GetAwaiter().GetResult();
    }
}

static void UdlRuntimeChannelsIncludeRegistryItems()
{
    var method = typeof(UdlClientControl).GetMethod("BuildRuntimeChannelDescriptors", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("BuildRuntimeChannelDescriptors was not found.");
    }

    var ownerItem = new FolderItemModel { Kind = ControlKind.UdlClientControl, Name = "udl_client_control" };
    ownerItem.SetHierarchy("default_layout", null);

    var runtimeChannelPath = "runtime.udl_client.udl_client_control.m310.read";
    HostRegistries.Data.Remove("runtime.udl_client.udl_client_control.m310");

    try
    {
        var runtimeChannel = ItemExtension.CreateWithPath(runtimeChannelPath);
        runtimeChannel.Properties["format"].Value = "b16";
        runtimeChannel.Properties["unit"].Value = "raw";
        HostRegistries.Data.UpsertSnapshot(runtimeChannelPath, runtimeChannel, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        var descriptors = ((IReadOnlyList<UdlRuntimeModuleChannelDescriptor>)method.Invoke(null, [ownerItem, Array.Empty<ItemModel>()])!).ToArray();
        var descriptor = descriptors.FirstOrDefault(candidate => string.Equals(candidate.ModuleName, "m310", StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.ChannelName, "read", StringComparison.OrdinalIgnoreCase));

        AssertTrue(descriptor is not null);
        AssertEqual("b16", descriptor!.Format);
        AssertEqual("raw", descriptor.Unit);
        AssertEqual(16, descriptor.BitCount);
    }
    finally
    {
        HostRegistries.Data.Remove("runtime.udl_client.udl_client_control.m310");
    }
}

static void UdlModulePublishesChannelTypeMetadata()
{
    var udlModuleType = typeof(SimulatedHostUdlClient).Assembly.GetType("HornetStudio.Host.UdlModule")
        ?? throw new InvalidOperationException("UdlModule type was not found.");
    var module = (ItemModel?)Activator.CreateInstance(udlModuleType, ["m001", "runtime.udl_client.udl_client_control"])
        ?? throw new InvalidOperationException("UdlModule instance could not be created.");
    var ensureWriteMetadata = udlModuleType.GetMethod("EnsureWriteMetadata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("UdlModule.EnsureWriteMetadata was not found.");

    AssertEqual("float", module["read"].Properties["type"].Value);
    AssertEqual("float", module["set"].Properties["type"].Value);
    AssertEqual("float", module["out"].Properties["type"].Value);
    AssertEqual("int", module["state"].Properties["type"].Value);
    AssertEqual("int", module["alert"].Properties["type"].Value);

    module["read"].Properties.Remove("type");
    module["set"].Properties.Remove("type");
    module["out"].Properties.Remove("type");
    module["state"].Properties.Remove("type");
    module["alert"].Properties.Remove("type");

    ensureWriteMetadata.Invoke(module, []);

    AssertEqual("float", module["read"].Properties["type"].Value);
    AssertEqual("float", module["set"].Properties["type"].Value);
    AssertEqual("float", module["out"].Properties["type"].Value);
    AssertEqual("int", module["state"].Properties["type"].Value);
    AssertEqual("int", module["alert"].Properties["type"].Value);
}

static void UdlRuntimeExposureBitsUseSnakeCasePaths()
{
    var method = typeof(UdlClientControl).GetMethod("UpsertRuntimeExposureBits", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("UpsertRuntimeExposureBits was not found.");
    }

    var control = (UdlClientControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UdlClientControl));
    var runtimeChannel = new ItemModel("read", path: "runtime.udl_client.udl_client_control.m002");
    runtimeChannel.Properties["read"].Value = 5;
    runtimeChannel.Properties["format"].Value = "b4";

    var definition = new UdlModuleExposureDefinition
    {
        ModuleName = "m002",
        ChannelName = "read",
        ExposeBits = true,
        BitCount = 4,
        BitLabels = "Bit0=Ready\nBit2=Fault"
    };

    method.Invoke(control, [runtimeChannel, definition, 4]);

    AssertTrue(runtimeChannel.Has("bits"));
    AssertTrue(runtimeChannel["bits"].Has("bit0"));
    AssertTrue(runtimeChannel.GetDictionary().ContainsKey("bits"));
    AssertTrue(runtimeChannel["bits"].GetDictionary().ContainsKey("bit0"));
    AssertEqual(true, runtimeChannel["bits"]["bit0"].Value);
    AssertEqual(false, runtimeChannel["bits"]["bit1"].Value);
    AssertEqual(true, runtimeChannel["bits"]["bit2"].Value);
    AssertEqual("Ready", runtimeChannel["bits"]["bit0"].Properties["title"].Value);
    AssertEqual("m002", runtimeChannel["bits"]["bit0"].Properties["module_name"].Value);
    AssertEqual("read", runtimeChannel["bits"]["bit0"].Properties["channel_name"].Value);
    AssertEqual(0, runtimeChannel["bits"]["bit0"].Properties["bit_index"].Value);
}

static void UdlExposureProjectionDetectsActiveBitExposures()
{
    AssertFalse(UdlClientExposureProjection.HasActiveBitExposures(Array.Empty<UdlModuleExposureDefinition>()));
    AssertFalse(UdlClientExposureProjection.HasActiveBitExposures(
    [
        new UdlModuleExposureDefinition
        {
            ModuleName = "m001",
            ChannelName = "read",
            RouteReadInputToSetRequest = true
        }
    ]));
    AssertTrue(UdlClientExposureProjection.HasActiveBitExposures(
    [
        new UdlModuleExposureDefinition
        {
            ModuleName = "m001",
            ChannelName = "read",
            ExposeBits = true
        }
    ]));
}

static void UdlSimulatedDemoSchedulerKeepsPeriodicCadenceUnderHandlerLoad()
{
    var definition = new UdlDemoModuleDefinition
    {
        Name = "m001",
        Kind = UdlDemoModuleKind.SetDriven,
        InitialValue = 0,
        SetScale = 1,
        SetOffset = 0,
        SetTauSeconds = 0
    };

    using var client = new SimulatedHostUdlClient(
        name: "udl_client_control",
        host: "demo",
        port: 9001,
        definitions: [definition]);

    var frameCount = 0;
    long firstFrameTimestamp = 0;
    client.FrameReceived += (_, _, _) =>
    {
        var count = Interlocked.Increment(ref frameCount);
        if (count == 1)
        {
            Interlocked.CompareExchange(ref firstFrameTimestamp, Stopwatch.GetTimestamp(), 0);
        }

        Thread.Sleep(80);
    };

    client.ConnectAsync().GetAwaiter().GetResult();
    try
    {
        var firstFrameObserved = SpinWait.SpinUntil(
            condition: () => Volatile.Read(ref firstFrameTimestamp) != 0,
            millisecondsTimeout: 1000);
        AssertTrue(firstFrameObserved);

        while (Stopwatch.GetElapsedTime(Volatile.Read(ref firstFrameTimestamp)) < TimeSpan.FromMilliseconds(430))
        {
            Thread.Sleep(10);
        }

        AssertTrue(Volatile.Read(ref frameCount) >= 4);
    }
    finally
    {
        client.DisconnectAsync().GetAwaiter().GetResult();
    }
}

static void UdlSimulatedDemoLowValueReadsSkipUnchangedAssignments()
{
    var method = typeof(SimulatedHostUdlClient).GetMethod("SetReadValueIfDifferent", BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("SimulatedHostUdlClient.SetReadValueIfDifferent was not found.");
    }

    var channel = new ItemModel("alert", path: "runtime.udl_client.udl_client_control.m001");
    var existingValue = new string("steady".ToCharArray());
    var matchingValue = new string("steady".ToCharArray());
    channel.Properties["read"].Value = existingValue;

    method.Invoke(null, [channel, matchingValue]);

    AssertTrue(object.ReferenceEquals(existingValue, channel.Properties["read"].Value));

    method.Invoke(null, [channel, "changed"]);

    AssertEqual("changed", channel.Properties["read"].Value);
}

static void SignalSourceOptionsIncludeDescendantsAndSkipStatusRoots()
{
    var attachedRootPath = "studio.default_layout.udl_client_control.m310";
    var statusRootPath = "studio.default_layout.udl_client_control.status";
    HostRegistries.Data.Remove(attachedRootPath);
    HostRegistries.Data.Remove(statusRootPath);

    try
    {
        var attachedRoot = ItemExtension.CreateWithPath(attachedRootPath);
        attachedRoot["read"].Value = 1;
        attachedRoot["read"]["bits"]["bit0"].Value = true;

        var statusRoot = ItemExtension.CreateWithPath(statusRootPath);
        statusRoot["connection"].Value = "Connected";

        HostRegistries.Data.UpsertSnapshot(attachedRootPath, attachedRoot, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);
        HostRegistries.Data.UpsertSnapshot(statusRootPath, statusRoot, DataRegistryItemMetadata.WidgetStatus(), pruneMissingMembers: true);

        var method = typeof(MainWindowViewModel).GetMethod("EnumerateSignalSourceOptions", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("EnumerateSignalSourceOptions was not found.");
        }

        var options = ((IEnumerable<string>)method.Invoke(null, [])!).ToArray();

        AssertTrue(options.Contains(attachedRootPath, StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains("studio.default_layout.udl_client_control.m310.read", StringComparer.OrdinalIgnoreCase));
        AssertTrue(options.Contains("studio.default_layout.udl_client_control.m310.read.bits.bit0", StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains(statusRootPath, StringComparer.OrdinalIgnoreCase));
        AssertFalse(options.Contains("studio.default_layout.udl_client_control.status.connection", StringComparer.OrdinalIgnoreCase));
    }
    finally
    {
        HostRegistries.Data.Remove(attachedRootPath);
        HostRegistries.Data.Remove(statusRootPath);
    }
}

static void TargetPathNormalizationUsesStudioRoot()
{
    var helperType = typeof(MainWindowViewModel).Assembly.GetType("HornetStudio.Editor.Helpers.TargetPathHelper");
    if (helperType is null)
    {
        throw new InvalidOperationException("TargetPathHelper was not found.");
    }

    var method = helperType.GetMethod("NormalizeConfiguredTargetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("NormalizeConfiguredTargetPath was not found.");
    }

    AssertEqual("studio.folder.x", method.Invoke(null, ["project.Folder.X"]));
    AssertEqual("studio.folder.x", method.Invoke(null, ["studio.Folder.X"]));
    AssertEqual("studio.folder.x", method.Invoke(null, ["studio.project.Folder.X"]));
}

static void BrokerPublishedItemCodecMigratesLegacyPaths()
{
    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions("project.default_layout.Edm1.Pressure");

    AssertEqual(1, parsed.Count);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].LocalPath);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].LocalRootPath);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].BrokerPath);
    AssertEqual(false, parsed[0].Active);
    AssertEqual(BrokerPublishedItemPublishModes.OnChanged, parsed[0].PublishMode);
    AssertEqual(1000, parsed[0].PublishIntervalMs);
    AssertEqual(false, parsed[0].Writable);
}

static IDisposable CreateUdlRuntime(
    string folderName,
    string clientName,
    bool demoEnabled,
    IReadOnlyList<UdlDemoModuleDefinition> definitions)
{
    var runtimeType = typeof(UdlClientControl).Assembly.GetType("HornetStudio.Editor.Widgets.UdlClientRuntime");
    if (runtimeType is null)
    {
        throw new InvalidOperationException("UdlClientRuntime type was not found.");
    }

    return (IDisposable)(Activator.CreateInstance(runtimeType, [folderName, clientName, "demo", 9001, demoEnabled, definitions, null])
        ?? throw new InvalidOperationException("UdlClientRuntime instance could not be created."));
}

static void ConnectUdlRuntime(IDisposable runtime)
{
    var task = (Task?)(runtime.GetType().GetMethod("ConnectAsync")?.Invoke(runtime, [CancellationToken.None]));
    if (task is null)
    {
        throw new InvalidOperationException("UdlClientRuntime.ConnectAsync was not found.");
    }

    task.GetAwaiter().GetResult();
}

static IReadOnlyList<string> GetUdlRuntimeReceivedRootPaths(IDisposable runtime)
{
    return (IReadOnlyList<string>)(runtime.GetType().GetMethod("GetReceivedRootPaths")?.Invoke(runtime, [])
        ?? throw new InvalidOperationException("UdlClientRuntime.GetReceivedRootPaths was not found."));
}

static IDisposable CreateUdlHostRegistryProjection(string folderName, string clientName)
{
    var projectionType = typeof(UdlClientControl).Assembly.GetType("HornetStudio.Editor.Widgets.UdlHostRegistryProjection");
    if (projectionType is null)
    {
        throw new InvalidOperationException("UdlHostRegistryProjection type was not found.");
    }

    return (IDisposable)(Activator.CreateInstance(projectionType, [folderName, clientName])
        ?? throw new InvalidOperationException("UdlHostRegistryProjection instance could not be created."));
}

static object GetUdlRuntimeAttachmentProjectionInput(IDisposable runtime, string attachedPaths)
{
    return runtime.GetType().GetMethod("GetAttachmentProjectionInput")?.Invoke(runtime, [attachedPaths])
        ?? throw new InvalidOperationException("UdlClientRuntime.GetAttachmentProjectionInput was not found.");
}

static ItemModel ResolveUdlRuntimeItem(IDisposable runtime, string relativePath)
{
    var method = runtime.GetType().GetMethod("TryResolveRuntimeItem");
    if (method is null)
    {
        throw new InvalidOperationException("UdlClientRuntime.TryResolveRuntimeItem was not found.");
    }

    var arguments = new object?[] { relativePath, null };
    var resolved = (bool?)(method.Invoke(runtime, arguments)) ?? false;
    if (!resolved || arguments[1] is not ItemModel item)
    {
        throw new InvalidOperationException($"UDL runtime item '{relativePath}' could not be resolved.");
    }

    return item;
}

static bool SynchronizeUdlHostRegistryProjection(IDisposable projection, object attachmentProjectionInput)
{
    return (bool?)(projection.GetType().GetMethod("SynchronizeAttachments")?.Invoke(projection, [attachmentProjectionInput]))
        ?? throw new InvalidOperationException("UdlHostRegistryProjection.SynchronizeAttachments was not found.");
}

static void BrokerPublishedItemCodecKeepsExplicitStudioBrokerPaths()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "project.default_layout.Edm1.Pressure",
            LocalRootPath = "project.default_layout.Edm1",
            BrokerPath = "studio.Custom.Path",
            Active = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("studio.custom.path", parsed[0].BrokerPath);
}

static void BrokerPublishedItemCodecKeepsExplicitHornetStudioBrokerPaths()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "project.default_layout.Edm1.Pressure",
            LocalRootPath = "project.default_layout.Edm1",
            BrokerPath = "HornetStudio.project.default_layout.Edm1.Pressure",
            Active = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("hornet_studio.project.default_layout.edm1.pressure", parsed[0].BrokerPath);
}

static void BrokerPublishedItemCodecRoundtrip()
{
    var serialized = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
    [
        new BrokerPublishedItemDefinition
        {
            LocalPath = "project.default_layout.Edm1.Pressure",
            LocalRootPath = "project.default_layout.Edm1",
            BrokerPath = "studio.project.default_layout.Edm1.Pressure",
            Active = true,
            PublishMode = BrokerPublishedItemPublishModes.Interval,
            PublishIntervalMs = 250,
            Writable = true
        }
    ]);

    var parsed = BrokerPublishedItemDefinitionCodec.ParseDefinitions(serialized);

    AssertEqual(1, parsed.Count);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].LocalPath);
    AssertEqual("studio.default_layout.edm1", parsed[0].LocalRootPath);
    AssertEqual("studio.default_layout.edm1.pressure", parsed[0].BrokerPath);
    AssertEqual(true, parsed[0].Active);
    AssertEqual(BrokerPublishedItemPublishModes.Interval, parsed[0].PublishMode);
    AssertEqual(250, parsed[0].PublishIntervalMs);
    AssertEqual(true, parsed[0].Writable);
}

static void BrokerPublishedItemCodecFiltersActiveRootDefinitions()
{
    var definitions = new[]
    {
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "project.default_layout.Edm1",
            LocalPath = "project.default_layout.Edm1.Pressure",
            BrokerPath = "studio.project.default_layout.Edm1.Pressure",
            Active = true
        },
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "project.default_layout.Edm1",
            LocalPath = "project.default_layout.Edm1.Temperature",
            BrokerPath = "studio.project.default_layout.Edm1.Temperature",
            Active = false
        },
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = "project.default_layout.Edm2",
            LocalPath = "project.default_layout.Edm2.Pressure",
            BrokerPath = "studio.project.default_layout.Edm2.Pressure",
            Active = true
        }
    };

    var filtered = BrokerPublishedItemDefinitionCodec.GetActiveDefinitionsForRoot(definitions, "project.default_layout.edm1");

    AssertEqual(1, filtered.Count);
    AssertEqual("studio.default_layout.edm1.pressure", filtered[0].LocalPath);
}

static void BrokerPublishedItemChangeMatcherScopesChanges()
{
    var rootDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "studio.default_layout.edm1",
        BrokerPath = "studio.default_layout.edm1",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var childDefinition = new BrokerPublishedItemDefinition
    {
        LocalPath = "studio.default_layout.edm1.pressure",
        BrokerPath = "studio.default_layout.edm1.pressure",
        Active = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged
    };
    var rootItem = ItemExtension.CreateWithPath("studio.default_layout.edm1");
    rootItem["pressure"].Value = 12.5;

    ItemModel? Resolve(string path)
    {
        if (string.Equals(path, "studio.default_layout.edm1", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem;
        }

        if (string.Equals(path, "studio.default_layout.edm1.pressure", StringComparison.OrdinalIgnoreCase))
        {
            return rootItem["pressure"];
        }

        return null;
    }

    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1.pressure", rootItem["pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        rootDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1.pressure", rootItem["pressure"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1", rootItem, DataChangeKind.SnapshotUpserted),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm1", rootItem, DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublish(
        childDefinition,
        new DataChangedEventArgs("studio.default_layout.edm2.pressure", rootItem["pressure"], DataChangeKind.ValueUpdated),
        Resolve));
}

static void BrokerPublishedItemChangeMatcherObservesResolvedWritableTargets()
{
    const string localPath = "studio.default_layout.edm1.command";
    const string writeTargetPath = "studio.default_layout.udl1.setpoint";

    var definition = new BrokerPublishedItemDefinition
    {
        LocalPath = localPath,
        BrokerPath = "studio.default_layout.edm1.command",
        Active = true,
        Writable = true,
        PublishMode = BrokerPublishedItemPublishModes.OnChanged,
    };

    var commandItem = ItemExtension.CreateWithPath(localPath, 1d);
    commandItem.Properties["write_path"].Value = writeTargetPath;
    var writeTargetRoot = ItemExtension.CreateWithPath("studio.default_layout.udl1");
    writeTargetRoot["setpoint"].Value = 21d;
    writeTargetRoot["setpoint"].Properties["write"].Value = 21d;

    ItemModel? Resolve(string path)
    {
        if (string.Equals(path, localPath, StringComparison.OrdinalIgnoreCase))
        {
            return commandItem;
        }

        if (string.Equals(path, writeTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return writeTargetRoot["setpoint"];
        }

        return null;
    }

    AssertTrue(BrokerPublishedItemChangeMatcher.ShouldObserveChange(
        definition,
        new DataChangedEventArgs(writeTargetPath, writeTargetRoot["setpoint"], DataChangeKind.ValueUpdated),
        Resolve));
    AssertFalse(BrokerPublishedItemChangeMatcher.ShouldPublishDefinitionChange(
        definition,
        new DataChangedEventArgs(writeTargetPath, writeTargetRoot["setpoint"], DataChangeKind.ValueUpdated),
        Resolve));
}

static void BrokerPublisherSendsValueUpdateForUnregisteredValueChange()
{
    var localPath = "runtime.broker_publisher.set.request";
    var brokerPath = "studio.folder1.udl_client1.m300.set.request";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

    var widget = new FolderItemModel
    {
        Kind = ControlKind.ItemClient,
        Name = "BrokerPublisher",
        BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
        [
            new BrokerPublishedItemDefinition
            {
                LocalRootPath = localPath,
                LocalPath = localPath,
                BrokerPath = brokerPath,
                PublishMode = BrokerPublishedItemPublishModes.OnChanged,
                Active = true,
                Writable = true,
            }
        ])
    };

    var client = new FakeHostItemBrokerClient();
    using var publisher = CreateBrokerPublisher(widget, client);
    StartBrokerPublisher(publisher, publishInitialSnapshots: false);

    AssertTrue(HostRegistries.Data.UpdateValue(localPath, 42));

    AssertEqual(0, client.PublishedSnapshots.Count);
    AssertEqual(1, client.ValueUpdates.Count);
    AssertEqual(brokerPath, client.ValueUpdates[0].Path);
    AssertEqual(42, client.ValueUpdates[0].Value);
}

static void BrokerPublisherSendsRepeatedWritePropertyCommand()
{
    var localPath = "studio.default_layout.enhanced_signals.filtered_1.set";
    var brokerPath = "studio.default_layout.enhanced_signals.filtered_1.set";
    var target = ItemExtension.CreateWithPath(localPath, 10d);
    target.Properties["writable"].Value = true;
    target.Properties["write"].Value = 1000000d;
    HostRegistries.Data.UpsertSnapshot(localPath, target);

    try
    {
        var widget = new FolderItemModel
        {
            Kind = ControlKind.ItemClient,
            Name = "BrokerPublisher",
            BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
            [
                new BrokerPublishedItemDefinition
                {
                    LocalRootPath = localPath,
                    LocalPath = localPath,
                    BrokerPath = brokerPath,
                    PublishMode = BrokerPublishedItemPublishModes.OnChanged,
                    Active = true,
                    Writable = true,
                }
            ])
        };

        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "Signal2Set",
            TargetPath = localPath,
        };

        var client = new FakeHostItemBrokerClient();
        using var publisher = CreateBrokerPublisher(widget, client);
        StartBrokerPublisher(publisher, publishInitialSnapshots: false);

        AssertTrue(signal.TryUpdateTargetPropertyValue(1000000d, out var error));
        AssertEqual(string.Empty, error);

        AssertEqual(0, client.PublishedSnapshots.Count);
        AssertEqual(0, client.ValueUpdates.Count);
        AssertEqual(1, client.ParameterUpdates.Count);
        AssertEqual(brokerPath, client.ParameterUpdates[0].ItemModel.Path);
        AssertEqual("write", client.ParameterUpdates[0].ParameterName);
        AssertEqual(1000000d, client.ParameterUpdates[0].ItemModel.Properties["write"].Value);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void BrokerPublisherRecordsLocalHostWriteStateForWritableValueChanges()
{
    var localPath = "runtime.broker_publish.local_write_state";
    var brokerPath = "studio.runtime.broker_publish.local_write_state";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

    try
    {
        var ownerItem = new FolderItemModel
        {
            BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
            [
                new BrokerPublishedItemDefinition
                {
                    LocalRootPath = localPath,
                    LocalPath = localPath,
                    BrokerPath = brokerPath,
                    Active = true,
                    Writable = true,
                    PublishMode = BrokerPublishedItemPublishModes.OnChanged,
                }
            ])
        };

        var recordedWrites = new List<string>();
        var client = new FakeHostItemBrokerClient();
        using var publisher = CreateBrokerPublisher(
            ownerItem,
            client,
            recordLocalHostWriteState: (targetPath, parameterName, value) => recordedWrites.Add($"{targetPath}|{parameterName}|{value}"));

        StartBrokerPublisher(publisher, publishInitialSnapshots: false);
        HostRegistries.Data.UpdateValue(localPath, 42);

        AssertEqual(1, recordedWrites.Count);
        AssertEqual($"{localPath}|read|42", recordedWrites[0]);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void BrokerWriteBackIgnoresStaleReadAfterRecentLocalHostWriteOnResolvedTarget()
{
    var localPath = "runtime.broker_write_back.host_priority_source";
    var writeTargetPath = "runtime.broker_write_back.host_priority_target";
    var brokerPath = "studio.runtime.broker_write_back.host_priority_source";
    var source = ItemExtension.CreateWithPath(localPath, 0d);
    source.Properties["write_path"].Value = writeTargetPath;
    var target = ItemExtension.CreateWithPath(writeTargetPath, 42d);
    target.Properties["write"].Value = 42d;
    HostRegistries.Data.UpsertSnapshot(localPath, source);
    HostRegistries.Data.UpsertSnapshot(writeTargetPath, target);

    try
    {
        var tracker = CreateLocalHostWriteTracker();
        RecordLocalHostWrite(tracker, writeTargetPath, "read", 42d);

        var client = new FakeHostItemBrokerClient();
        using var writeBack = CreateWriteBackClient(
            client,
            localPath,
            brokerPath,
            active: true,
            writable: true,
            hasRecentLocalHostWriteConflict: (targetPath, parameterName, value) => HasRecentLocalHostWriteConflict(tracker, targetPath, parameterName, value));
        writeBack.StartAsync().GetAwaiter().GetResult();

        client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 5d, "external-client", null, DateTimeOffset.UtcNow));

        AssertTrue(HostRegistries.Data.TryResolve(writeTargetPath, out var resolved));
        AssertEqual(42d, resolved?.Value);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
        HostRegistries.Data.Remove(writeTargetPath);
    }
}

static void BrokerWriteBackTreatsSameValueResolvedTargetEchoesAsNonConflicts()
{
    var localPath = "runtime.broker_write_back.same_value_source";
    var writeTargetPath = "runtime.broker_write_back.same_value_target";
    var brokerPath = "studio.runtime.broker_write_back.same_value_source";
    var source = ItemExtension.CreateWithPath(localPath, 0d);
    source.Properties["write_path"].Value = writeTargetPath;
    var target = ItemExtension.CreateWithPath(writeTargetPath, 42d);
    target.Properties["write"].Value = 42d;
    HostRegistries.Data.UpsertSnapshot(localPath, source);
    HostRegistries.Data.UpsertSnapshot(writeTargetPath, target);

    try
    {
        var tracker = CreateLocalHostWriteTracker();
        RecordLocalHostWrite(tracker, writeTargetPath, "read", 42d);

        var client = new FakeHostItemBrokerClient();
        using var writeBack = CreateWriteBackClient(
            client,
            localPath,
            brokerPath,
            active: true,
            writable: true,
            hasRecentLocalHostWriteConflict: (targetPath, parameterName, value) => HasRecentLocalHostWriteConflict(tracker, targetPath, parameterName, value));
        writeBack.StartAsync().GetAwaiter().GetResult();

        client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42d, "external-client", null, DateTimeOffset.UtcNow));

        AssertTrue(HostRegistries.Data.TryResolve(writeTargetPath, out var resolved));
        AssertEqual(42d, resolved?.Value);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
        HostRegistries.Data.Remove(writeTargetPath);
    }
}

static void BrokerPublisherOmitsWritePropertiesFromRetainedSnapshots()
{
    var localPath = "runtime.broker_publisher.write_snapshot";
    var brokerPath = "studio.folder1.write_snapshot";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["unit"].Value = "bar";
    item.Properties["write"].Value = 10;
    item.AddItem("child");
    item["child"].Value = 2;
    item["child"].Properties["unit"].Value = "deg_c";
    item["child"].Properties["write"].Value = 20;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    try
    {
        var widget = new FolderItemModel
        {
            Kind = ControlKind.ItemClient,
            Name = "BrokerPublisher",
            BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
            [
                new BrokerPublishedItemDefinition
                {
                    LocalRootPath = localPath,
                    LocalPath = localPath,
                    BrokerPath = brokerPath,
                    PublishMode = BrokerPublishedItemPublishModes.OnChanged,
                    Active = true,
                    Writable = true,
                }
            ])
        };

        var client = new FakeHostItemBrokerClient();
        using var publisher = CreateBrokerPublisher(widget, client);
        StartBrokerPublisher(publisher, publishInitialSnapshots: true);

        AssertEqual(1, client.PublishedSnapshots.Count);
        AssertEqual(brokerPath, client.PublishedSnapshots[0].Path);
        AssertTrue(client.PublishedSnapshots[0].Properties.Has("unit"));
        AssertFalse(client.PublishedSnapshots[0].Properties.Has("write"));
        AssertFalse(client.PublishedSnapshots[0].GetDictionary().ContainsKey("child"));
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void BrokerPublisherSkipsChildItemsAndNonMqttSnapshotProperties()
{
    var localPath = "runtime.broker_publisher.invalid_property_snapshot";
    var brokerPath = "studio.folder1.invalid_property_snapshot";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["valid_property"].Value = "ok";
    item.Properties["InvalidProperty"].Value = "skip";
    item.AddItem("child");
    item["child"].Properties["child_property"].Value = "ok";
    item["child"].Properties["ChildProperty"].Value = "skip";
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    try
    {
        var widget = new FolderItemModel
        {
            Kind = ControlKind.ItemClient,
            Name = "BrokerPublisher",
            BrokerPublishedItemPaths = BrokerPublishedItemDefinitionCodec.SerializeDefinitions(
            [
                new BrokerPublishedItemDefinition
                {
                    LocalRootPath = localPath,
                    LocalPath = localPath,
                    BrokerPath = brokerPath,
                    PublishMode = BrokerPublishedItemPublishModes.OnChanged,
                    Active = true,
                }
            ])
        };

        var client = new FakeHostItemBrokerClient();
        using var publisher = CreateBrokerPublisher(widget, client);
        StartBrokerPublisher(publisher, publishInitialSnapshots: true);

        AssertEqual(1, client.PublishedSnapshots.Count);
        AssertTrue(client.PublishedSnapshots[0].Properties.Has("valid_property"));
        AssertFalse(client.PublishedSnapshots[0].Properties.Has("InvalidProperty"));
        AssertFalse(client.PublishedSnapshots[0].GetDictionary().ContainsKey("child"));
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void SignalWriteEmitsRegistryValueUpdate()
{
    var targetPath = "studio.editor_tests.signal_write.demo1";
    var target = ItemExtension.CreateWithPath(targetPath, 80d);
    target.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(targetPath, target);

    var signal = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "Demo1",
        TargetPath = targetPath,
    };

    var valueUpdateCount = 0;
    HostRegistries.Data.ItemChanged += OnItemChanged;
    try
    {
        AssertTrue(signal.TryUpdateTargetPropertyValue(10d, out var error));
        AssertEqual(string.Empty, error);
        AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var resolved));
        AssertEqual(10d, resolved?.Value);
        AssertEqual(1, valueUpdateCount);
    }
    finally
    {
        HostRegistries.Data.ItemChanged -= OnItemChanged;
    }

    void OnItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind == DataChangeKind.ValueUpdated
            && string.Equals(e.Key, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            valueUpdateCount++;
        }
    }
}

static void SignalWriteUpdatesWritePropertyWhenPresent()
{
    var targetPath = "studio.editor_tests.signal_write.demo_write_property";
    var target = ItemExtension.CreateWithPath(targetPath, 80d);
    target.Properties["read"].Value = 80d;
    target.Properties["write"].Value = 80d;
    HostRegistries.Data.UpsertSnapshot(targetPath, target);

    var signal = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "DemoWriteProperty",
        TargetPath = targetPath,
    };

    AssertEqual("read", signal.TargetPropertyPath);
    AssertTrue(signal.TryUpdateTargetPropertyValue(10d, out var error));
    AssertEqual(string.Empty, error);
    AssertTrue(HostRegistries.Data.TryResolve(targetPath, out var resolved));
    AssertEqual(80d, resolved?.Value);
    AssertEqual(80d, resolved?.Properties["read"].Value);
    AssertEqual(10d, resolved?.Properties["write"].Value);
}

static void SignalTargetPropertyResolvesValueReferenceRuntimeValue()
{
    const string publicPath = "studio.editor_tests.signal_value_ref.demo.read";
    const string runtimePath = "runtime.editor_tests.signal_value_ref.demo.read";

    var publicItem = ItemExtension.CreateWithPath(publicPath, 5d);
    publicItem.Properties["read"].Value = 5d;
    publicItem.Properties["valueRefPath"].Value = runtimePath;
    publicItem.Properties["valueRefParameter"].Value = "read";

    var runtimeItem = ItemExtension.CreateWithPath(runtimePath, 42d);
    runtimeItem.Properties["read"].Value = 42d;

    HostRegistries.Data.UpsertSnapshot(publicPath, publicItem);
    HostRegistries.Data.UpsertSnapshot(runtimePath, runtimeItem);
    HostRegistries.Data.RegisterValueReference(new DataRegistryValueReference(
        PublicItemPath: publicPath,
        PublicParameterName: "read",
        SourceItemPath: runtimePath,
        SourceParameterName: "read"));

    try
    {
        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "ValueRefSignal",
            TargetPath = publicPath,
        };

        AssertEqual("read", signal.TargetPropertyView.Property?.Name);
        AssertEqual(42d, signal.TargetPropertyView.Property?.Value);
    }
    finally
    {
        HostRegistries.Data.RemoveValueReference(publicPath, "read");
        HostRegistries.Data.Remove(publicPath);
        HostRegistries.Data.Remove(runtimePath);
    }
}

static void SignalPublicReadUpdatePreservesValueReferenceResolution()
{
    const string publicPath = "studio.editor_tests.signal_value_ref.public_update.read";
    const string runtimePath = "runtime.editor_tests.signal_value_ref.public_update.read";

    var publicItem = ItemExtension.CreateWithPath(publicPath, 5d);
    publicItem.Properties["read"].Value = 5d;
    publicItem.Properties["valueRefPath"].Value = runtimePath;
    publicItem.Properties["valueRefParameter"].Value = "read";

    var runtimeItem = ItemExtension.CreateWithPath(runtimePath, 42d);
    runtimeItem.Properties["read"].Value = 42d;

    HostRegistries.Data.UpsertSnapshot(publicPath, publicItem);
    HostRegistries.Data.UpsertSnapshot(runtimePath, runtimeItem);
    HostRegistries.Data.RegisterValueReference(new DataRegistryValueReference(
        PublicItemPath: publicPath,
        PublicParameterName: "read",
        SourceItemPath: runtimePath,
        SourceParameterName: "read"));

    try
    {
        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "ValueRefPublicUpdateSignal",
            TargetPath = publicPath,
        };

        AssertEqual(42d, signal.TargetPropertyView.Property?.Value);

        AssertTrue(HostRegistries.Data.UpdateProperty(publicPath, "read", 9d));

        AssertEqual(42d, signal.TargetPropertyView.Property?.Value);
    }
    finally
    {
        HostRegistries.Data.RemoveValueReference(publicPath, "read");
        HostRegistries.Data.Remove(publicPath);
        HostRegistries.Data.Remove(runtimePath);
    }
}

static void SignalTargetPropertyFallsBackWhenValueReferenceIsInvalid()
{
    const string publicPath = "studio.editor_tests.signal_value_ref.invalid.read";

    var publicItem = ItemExtension.CreateWithPath(publicPath, 7d);
    publicItem.Properties["read"].Value = 7d;
    publicItem.Properties["valueRefPath"].Value = "runtime.editor_tests.signal_value_ref.invalid.read";
    publicItem.Properties["valueRefParameter"].Value = "missing";

    HostRegistries.Data.UpsertSnapshot(publicPath, publicItem);

    try
    {
        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "FallbackSignal",
            TargetPath = publicPath,
        };

        AssertEqual("read", signal.TargetPropertyView.Property?.Name);
        AssertEqual(7d, signal.TargetPropertyView.Property?.Value);
    }
    finally
    {
        HostRegistries.Data.Remove(publicPath);
    }
}

static void SignalWriteKeepsPublicWritebackWithValueReference()
{
    const string publicPath = "studio.editor_tests.signal_value_ref.write.read";
    const string runtimePath = "runtime.editor_tests.signal_value_ref.write.read";

    var publicItem = ItemExtension.CreateWithPath(publicPath, 80d);
    publicItem.Properties["read"].Value = 80d;
    publicItem.Properties["write"].Value = 80d;
    publicItem.Properties["valueRefPath"].Value = runtimePath;
    publicItem.Properties["valueRefParameter"].Value = "read";

    var runtimeItem = ItemExtension.CreateWithPath(runtimePath, 15d);
    runtimeItem.Properties["read"].Value = 15d;

    HostRegistries.Data.UpsertSnapshot(publicPath, publicItem);
    HostRegistries.Data.UpsertSnapshot(runtimePath, runtimeItem);
    HostRegistries.Data.RegisterValueReference(new DataRegistryValueReference(
        PublicItemPath: publicPath,
        PublicParameterName: "read",
        SourceItemPath: runtimePath,
        SourceParameterName: "read"));

    try
    {
        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "ValueRefWriteSignal",
            TargetPath = publicPath,
        };

        AssertEqual(15d, signal.TargetPropertyView.Property?.Value);
        AssertTrue(signal.TryUpdateTargetPropertyValue(10d, out var error));
        AssertEqual(string.Empty, error);
        AssertTrue(HostRegistries.Data.TryResolve(publicPath, out var resolvedPublic));
        AssertTrue(HostRegistries.Data.TryResolve(runtimePath, out var resolvedRuntime));
        AssertEqual(10d, resolvedPublic?.Properties["write"].Value);
        AssertEqual(15d, resolvedRuntime?.Properties["read"].Value);
    }
    finally
    {
        HostRegistries.Data.RemoveValueReference(publicPath, "read");
        HostRegistries.Data.Remove(publicPath);
        HostRegistries.Data.Remove(runtimePath);
    }
}

static void SignalLiveValueStoreResolvesUdlValueReference()
{
    const string publicPath = "studio.main.signal_live_value.udl1.m001.read";
    const string runtimePath = "runtime.udl_client.udl1.m001.read";

    var publicItem = ItemExtension.CreateWithPath(publicPath, 5d);
    publicItem.Properties["read"].Value = 5d;
    publicItem.Properties["valueRefPath"].Value = runtimePath;
    publicItem.Properties["valueRefParameter"].Value = "read";

    HostRegistries.Data.UpsertSnapshot(publicPath, publicItem);
    UdlClientLiveValueStore.ClearScope("main", "udl1");
    UdlClientLiveValueStore.UpdateProperty("main", "udl1", runtimePath, "read", 42d);

    try
    {
        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "LiveValueStoreSignal",
            TargetPath = publicPath,
        };

        AssertEqual(42d, signal.TargetPropertyView.Property?.Value);
    }
    finally
    {
        UdlClientLiveValueStore.ClearScope("main", "udl1");
        HostRegistries.Data.Remove(publicPath);
    }
}

static void SignalLiveValueSchedulerAppliesLatestWins()
{
    const string publicPath = "studio.main.signal_live_value.udl1.m002.read";
    const string runtimePath = "runtime.udl_client.udl1.m002.read";

    var publicItem = ItemExtension.CreateWithPath(publicPath, 1d);
    publicItem.Properties["read"].Value = 1d;
    publicItem.Properties["valueRefPath"].Value = runtimePath;
    publicItem.Properties["valueRefParameter"].Value = "read";

    HostRegistries.Data.UpsertSnapshot(publicPath, publicItem);
    UdlClientLiveValueStore.ClearScope("main", "udl1");

    try
    {
        var signal = new FolderItemModel
        {
            Kind = ControlKind.Signal,
            Name = "LiveValueSchedulerSignal",
            TargetPath = publicPath,
        };

        UdlClientLiveValueStore.UpdateProperty("main", "udl1", runtimePath, "read", 2d);
        UdlClientLiveValueStore.UpdateProperty("main", "udl1", runtimePath, "read", 3d);

        AssertTrue(signal.ApplyScheduledSignalValueRefresh(DateTimeOffset.UtcNow));
        AssertEqual(3d, signal.TargetPropertyView.Property?.Value);
        AssertFalse(signal.ApplyScheduledSignalValueRefresh(DateTimeOffset.UtcNow));
    }
    finally
    {
        UdlClientLiveValueStore.ClearScope("main", "udl1");
        HostRegistries.Data.Remove(publicPath);
    }
}

static void SignalAncestorReadPropertyChangeUsesValueOnlyRefresh()
{
    const string rootPath = "studio.editor_tests.signal_refresh.demo1";
    const string targetPath = rootPath + ".read";

    var rootItem = ItemExtension.CreateWithPath(rootPath);
    rootItem["read"].Value = 12d;

    var signal = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "DemoSignal",
        TargetPath = targetPath,
        TargetPropertyPath = "read",
    };

    SetSignalTarget(signal, rootItem["read"]);

    var method = typeof(FolderItemModel).GetMethod("IsTargetValueOnlyChange", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method is null)
    {
        throw new InvalidOperationException("FolderItemModel.IsTargetValueOnlyChange was not found.");
    }

    var result = (bool?)method.Invoke(signal, [
        new DataChangedEventArgs(rootPath, rootItem, DataChangeKind.PropertyUpdated, "read"),
        targetPath,
        false,
        true]) ?? false;

    AssertTrue(result);
}

static void SignalAncestorReadPropertyQueueStoresChildTarget()
{
    const string rootPath = "studio.editor_tests.signal_refresh.demo_queue";
    const string targetPath = rootPath + ".read";

    var rootItem = ItemExtension.CreateWithPath(rootPath);
    rootItem["read"].Value = 34d;

    var signal = new FolderItemModel
    {
        Kind = ControlKind.Signal,
        Name = "DemoSignalQueue",
        TargetPath = targetPath,
        TargetPropertyPath = "read",
    };

    SetSignalTarget(signal, rootItem["read"]);

    var queueMethod = typeof(FolderItemModel).GetMethod("QueueTargetValueRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
    if (queueMethod is null)
    {
        throw new InvalidOperationException("FolderItemModel.QueueTargetValueRefresh was not found.");
    }

    queueMethod.Invoke(signal, [
        new DataChangedEventArgs(rootPath, rootItem, DataChangeKind.PropertyUpdated, "read"),
        targetPath]);

    var pendingField = typeof(FolderItemModel).GetField("_pendingTargetValueRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
    if (pendingField is null)
    {
        throw new InvalidOperationException("FolderItemModel._pendingTargetValueRefresh was not found.");
    }

    var pendingRefresh = pendingField.GetValue(signal)
        ?? throw new InvalidOperationException("Pending target value refresh was not captured.");
    var itemProperty = pendingRefresh.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
    if (itemProperty is null)
    {
        throw new InvalidOperationException("PendingTargetValueRefresh.Item was not found.");
    }

    var queuedItem = itemProperty.GetValue(pendingRefresh) as ItemModel;
    AssertTrue(queuedItem is not null);
    AssertEqual(targetPath, queuedItem?.Path);
}

static void SetSignalTarget(FolderItemModel signal, ItemModel target)
{
    var property = typeof(FolderItemModel).GetProperty("Target", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    if (property is null)
    {
        throw new InvalidOperationException("FolderItemModel.Target was not found.");
    }

    property.SetValue(signal, target);
}

static void BrokerWriteBackIgnoresNonWritableEntries()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "runtime.broker_write_back.non_writable",
        "studio.runtime.broker_write_back.non_writable",
        active: true,
        writable: false);

    writeBack.StartAsync().GetAwaiter().GetResult();

    AssertEqual(0, client.Subscriptions.Count);
}

static void BrokerWriteBackIgnoresInactiveEntries()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "runtime.broker_write_back.inactive",
        "studio.runtime.broker_write_back.inactive",
        active: false,
        writable: true);

    writeBack.StartAsync().GetAwaiter().GetResult();

    AssertEqual(0, client.Subscriptions.Count);
}

static void BrokerWriteBackUpdatesWritableValue()
{
    var localPath = "runtime.broker_write_back.value";
    var brokerPath = "studio.runtime.broker_write_back.value";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(42, resolved?.Value);
}

static void BrokerWriteBackAppliesWriteRequests()
{
    var localPath = "runtime.broker_write_back.write_request";
    var brokerPath = "studio.runtime.broker_write_back.write_request";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["write"].Value = 1;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 23, null, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(1, resolved?.Value);
    AssertEqual(23, resolved?.Properties["write"].Value);
}

static void BrokerWriteBackIgnoresOwnWriteRequestEchoOnce()
{
    var localPath = "runtime.broker_write_back.own_write_echo";
    var brokerPath = "studio.runtime.broker_write_back.own_write_echo";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["write"].Value = 10;
    HostRegistries.Data.UpsertSnapshot(localPath, item);
    var writeUpdateCount = 0;

    try
    {
        var client = new FakeHostItemBrokerClient();
        var pendingOwnEchoes = 1;
        using var writeBack = new HostItemBrokerWriteBackClient(
            client,
            [
                new BrokerPublishedItemDefinition
                {
                    LocalRootPath = localPath,
                    LocalPath = localPath,
                    BrokerPath = brokerPath,
                    Active = true,
                    Writable = true,
                }
            ],
            (path, parameter, value) =>
            {
                if (pendingOwnEchoes <= 0
                    || !string.Equals(path, brokerPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(parameter, "write", StringComparison.OrdinalIgnoreCase)
                    || !Equals(value, 10))
                {
                    return false;
                }

                pendingOwnEchoes--;
                return true;
            });
        writeBack.StartAsync().GetAwaiter().GetResult();

        HostRegistries.Data.ItemChanged += OnItemChanged;
        try
        {
            client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 10, null, null, null, DateTimeOffset.UtcNow));
            AssertEqual(0, writeUpdateCount);

            client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 10, null, null, null, DateTimeOffset.UtcNow));
            AssertEqual(1, writeUpdateCount);
        }
        finally
        {
            HostRegistries.Data.ItemChanged -= OnItemChanged;
        }
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }

    void OnItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind == DataChangeKind.PropertyUpdated
            && string.Equals(e.Key, localPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ParameterName, "write", StringComparison.OrdinalIgnoreCase))
        {
            writeUpdateCount++;
        }
    }
}

static void BrokerWriteBackNotifiesRepeatedWriteRequests()
{
    var localPath = "runtime.broker_write_back.repeated_write_request";
    var brokerPath = "studio.runtime.broker_write_back.repeated_write_request";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["write"].Value = 10;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    var writeUpdateCount = 0;
    HostRegistries.Data.ItemChanged += OnItemChanged;
    try
    {
        client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 10, null, "external-client", null, DateTimeOffset.UtcNow));
        client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 10, null, "external-client", null, DateTimeOffset.UtcNow));

        AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
        AssertEqual(10, resolved?.Properties["write"].Value);
        AssertEqual(2, writeUpdateCount);
    }
    finally
    {
        HostRegistries.Data.ItemChanged -= OnItemChanged;
        HostRegistries.Data.Remove(localPath);
    }

    void OnItemChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind == DataChangeKind.PropertyUpdated
            && string.Equals(e.Key, localPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ParameterName, "write", StringComparison.OrdinalIgnoreCase))
        {
            writeUpdateCount++;
        }
    }
}

static void BrokerWriteBackIgnoresPropertyStyleWriteState()
{
    var localPath = "runtime.broker_write_back.write_property_state";
    var brokerPath = "studio.runtime.broker_write_back.write_property_state";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["write"].Value = 1;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    try
    {
        var client = new FakeHostItemBrokerClient();
        using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
        writeBack.StartAsync().GetAwaiter().GetResult();

        client.PublishToSubscription(new ItemPropertyChangedMessage(brokerPath, "write", 1000, "external-client", null, DateTimeOffset.UtcNow));

        AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
        AssertEqual(1, resolved?.Properties["write"].Value);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void BrokerWriteBackIgnoresStaleReadAfterRecentLocalHostWrite()
{
    var localPath = "runtime.broker_write_back.host_priority";
    var brokerPath = "studio.runtime.broker_write_back.host_priority";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 42));

    try
    {
        var tracker = CreateLocalHostWriteTracker();
        RecordLocalHostWrite(tracker, localPath, "read", 42);

        var client = new FakeHostItemBrokerClient();
        using var writeBack = CreateWriteBackClient(
            client,
            localPath,
            brokerPath,
            active: true,
            writable: true,
            hasRecentLocalHostWriteConflict: (targetPath, parameterName, value) => HasRecentLocalHostWriteConflict(tracker, targetPath, parameterName, value));
        writeBack.StartAsync().GetAwaiter().GetResult();

        client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 5, "external-client", null, DateTimeOffset.UtcNow));

        AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
        AssertEqual(42, resolved?.Value);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void BrokerWriteBackAppliesUncachedSourceWriteRequests()
{
    var localPath = "runtime.broker_write_back.write_request_uncached_source";
    var brokerPath = "studio.runtime.broker_write_back.write_request_uncached_source";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["write"].Value = 1;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemPropertyChangedMessage(brokerPath, "write", 10, null, null, DateTimeOffset.UtcNow));
    HostRegistries.Data.UpdateProperty(localPath, "write", 1);
    client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 10, null, null, null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(10, resolved?.Properties["write"].Value);
}

static void BrokerWriteBackKeepsExternalWriteRequestsEnabledAfterRecentLocalHostWrite()
{
    var localPath = "runtime.broker_write_back.host_priority_write_request";
    var brokerPath = "studio.runtime.broker_write_back.host_priority_write_request";
    var item = ItemExtension.CreateWithPath(localPath, 42);
    item.Properties["write"].Value = 42;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    try
    {
        var tracker = CreateLocalHostWriteTracker();
        RecordLocalHostWrite(tracker, localPath, "write", 42);

        var client = new FakeHostItemBrokerClient();
        using var writeBack = CreateWriteBackClient(
            client,
            localPath,
            brokerPath,
            active: true,
            writable: true,
            hasRecentLocalHostWriteConflict: (targetPath, parameterName, value) => HasRecentLocalHostWriteConflict(tracker, targetPath, parameterName, value));
        writeBack.StartAsync().GetAwaiter().GetResult();

        client.PublishToSubscription(new ItemWriteRequestMessage(brokerPath, "write", 23, null, "external-client", null, DateTimeOffset.UtcNow));

        AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
        AssertEqual(23, resolved?.Properties["write"].Value);
    }
    finally
    {
        HostRegistries.Data.Remove(localPath);
    }
}

static void BrokerWriteBackNormalizesLegacyRequestMode()
{
    var localPath = "runtime.broker_write_back.request_value";
    var brokerPath = "studio.runtime.broker_write_back.request_value";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.AddItem("request");
    item["request"].Value = 1;
    item.Properties["writable"].Value = true;
    item.Properties["write_path"].Value = localPath;
    item.Properties["write_mode"].Value = SignalWriteMode.Request.ToString();
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 42, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(42, resolved?.Value);
    AssertTrue(HostRegistries.Data.TryResolve($"{localPath}.request", out var request));
    AssertEqual(1, request?.Value);
}

static void BrokerWriteBackConvertsNumericValueToLocalType()
{
    var localPath = "runtime.broker_write_back.double_value";
    var brokerPath = "studio.runtime.broker_write_back.double_value";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1.5));

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 2L, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(2.0, resolved?.Value);
}

static void BrokerWriteBackBlocksProtectedProperties()
{
    var localPath = "runtime.broker_write_back.protected_parameter";
    var brokerPath = "studio.runtime.broker_write_back.protected_parameter";
    var item = ItemExtension.CreateWithPath(localPath, 1);
    item.Properties["writable"].Value = true;
    HostRegistries.Data.UpsertSnapshot(localPath, item);

    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemPropertyChangedMessage(brokerPath, "Writable", false, "external-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(true, resolved?.Properties["writable"].Value);
}

static void BrokerWriteBackIgnoresSameValueSelfEchoes()
{
    var localPath = "runtime.broker_write_back.same_value";
    var brokerPath = "studio.runtime.broker_write_back.same_value";
    HostRegistries.Data.UpsertSnapshot(localPath, ItemExtension.CreateWithPath(localPath, 1));

    var client = new FakeHostItemBrokerClient { ClientIdValue = "own-client" };
    using var writeBack = CreateWriteBackClient(client, localPath, brokerPath, active: true, writable: true);
    writeBack.StartAsync().GetAwaiter().GetResult();

    client.PublishToSubscription(new ItemValueChangedMessage(brokerPath, 1L, "own-client", null, DateTimeOffset.UtcNow));

    AssertTrue(HostRegistries.Data.TryResolve(localPath, out var resolved));
    AssertEqual(1, resolved?.Value);
}

static void BrokerWriteBackCleanupDisposesSubscriptions()
{
    var client = new FakeHostItemBrokerClient();
    using var writeBack = CreateWriteBackClient(
        client,
        "runtime.broker_write_back.cleanup",
        "studio.runtime.broker_write_back.cleanup",
        active: true,
        writable: true);

    writeBack.StartAsync().GetAwaiter().GetResult();
    AssertEqual(1, client.Subscriptions.Count);

    writeBack.DisposeAsync().AsTask().GetAwaiter().GetResult();

    AssertTrue(client.Subscriptions[0].Disposed);
}

static HostItemBrokerWriteBackClient CreateWriteBackClient(
    FakeHostItemBrokerClient client,
    string localPath,
    string brokerPath,
    bool active,
    bool writable,
    Func<string, string, object?, bool>? hasRecentLocalHostWriteConflict = null)
    => new(client,
    [
        new BrokerPublishedItemDefinition
        {
            LocalRootPath = localPath,
            LocalPath = localPath,
            BrokerPath = brokerPath,
            Active = active,
            Writable = writable,
        }
    ],
    tryConsumeOwnWriteEcho: null,
    hasRecentLocalHostWriteConflict: hasRecentLocalHostWriteConflict);

static IDisposable CreateBrokerPublisher(
    FolderItemModel item,
    IHostItemBrokerClient client,
    Action<string, string, object?>? recordOwnWriteCommand = null,
    Action<string, string, object?>? recordLocalHostWriteState = null)
{
    var publisherType = typeof(ItemClientControl).GetNestedType("HostItemBrokerPublisher", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Broker publisher type was not found.");
    var constructor = publisherType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(FolderItemModel), typeof(IHostItemBrokerClient), typeof(Action<string, string, object?>), typeof(Action<string, string, object?>)],
        modifiers: null)
        ?? publisherType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(FolderItemModel), typeof(IHostItemBrokerClient), typeof(Action<string, string, object?>)],
        modifiers: null)
        ?? throw new InvalidOperationException("Broker publisher constructor was not found.");

    object?[] parameters = constructor.GetParameters().Length == 4
        ? [item, client, recordOwnWriteCommand ?? NoopRecordOwnWriteCommand, recordLocalHostWriteState ?? NoopRecordOwnWriteCommand]
        : [item, client, recordOwnWriteCommand ?? NoopRecordOwnWriteCommand];
    return (IDisposable)constructor.Invoke(parameters);
}

static void NoopRecordOwnWriteCommand(string brokerPath, string parameterName, object? value)
{
}

static object CreateLocalHostWriteTracker()
{
    var trackerType = typeof(ItemClientControl).GetNestedType("LocalHostWriteTracker", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("LocalHostWriteTracker type was not found.");
    return Activator.CreateInstance(trackerType)
        ?? throw new InvalidOperationException("LocalHostWriteTracker could not be created.");
}

static void RecordLocalHostWrite(object tracker, string targetPath, string parameterName, object? value)
{
    var method = tracker.GetType().GetMethod("Record", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("LocalHostWriteTracker.Record was not found.");
    method.Invoke(tracker, [targetPath, parameterName, value]);
}

static bool HasRecentLocalHostWriteConflict(object tracker, string targetPath, string parameterName, object? value)
{
    var method = tracker.GetType().GetMethod("HasRecentConflict", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("LocalHostWriteTracker.HasRecentConflict was not found.");
    return (bool)(method.Invoke(tracker, [targetPath, parameterName, value])
        ?? throw new InvalidOperationException("LocalHostWriteTracker.HasRecentConflict returned null."));
}

static void StartBrokerPublisher(IDisposable publisher, bool publishInitialSnapshots)
{
    var method = publisher.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Broker publisher start method was not found.");
    method.Invoke(publisher, [publishInitialSnapshots]);
}

static void ItemExposurePublisherAppliesBitHelpers()
{
    var item = new ItemModel("mask", 5, "runtime.item_broker.broker1.client1.device");
    var definition = new ItemExposureDefinition
    {
        ItemPath = "broker1.client1.device.mask",
        Format = "b4",
        Unit = "flags",
        ExposeBits = true,
        BitCount = 4,
        BitLabels = "Bit0=Ready\nBit2=Fault"
    };

    ItemExposurePublisher.Apply(item, definition);

    AssertEqual("b4", item.Properties["format"].Value);
    AssertEqual("flags", item.Properties["unit"].Value);
    AssertTrue(item.Has("bits"));
    AssertTrue(item["bits"].Has("bit0"));
    AssertTrue(item["bits"].Has("bit2"));
    AssertEqual(true, item["bits"]["bit0"].Value);
    AssertEqual(false, item["bits"]["bit1"].Value);
    AssertEqual(true, item["bits"]["bit2"].Value);
    AssertEqual("Ready", item["bits"]["bit0"].Properties["title"].Value);
    AssertEqual("Fault", item["bits"]["bit2"].Properties["title"].Value);
}

static void ItemExposureCodecUpsertAndRemove()
{
    var serialized = ItemExposureDefinitionCodec.SerializeDefinitions(
    [
        new ItemExposureDefinition
        {
            ItemPath = "device.mask",
            Format = "b4",
            ExposeBits = true,
            BitCount = 4
        },
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.value",
            Unit = "V"
        }
    ]);

    var upserted = ItemExposureDefinitionCodec.UpsertDefinition(
        serialized,
        "broker1.client1.device.mask",
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.mask",
            Format = "b8",
            ExposeBits = true,
            BitCount = 8
        });
    var parsedUpserted = ItemExposureDefinitionCodec.ParseDefinitions(upserted);
    AssertEqual(2, parsedUpserted.Count);
    AssertEqual("b8", parsedUpserted.Single(definition => definition.ItemPath.EndsWith("mask", StringComparison.OrdinalIgnoreCase)).Format);

    var removed = ItemExposureDefinitionCodec.RemoveDefinition(upserted, "broker1.client1.device.mask");
    var parsedRemoved = ItemExposureDefinitionCodec.ParseDefinitions(removed);
    AssertEqual(1, parsedRemoved.Count);
    AssertEqual("broker1.client1.device.value", parsedRemoved[0].ItemPath);
}

static void ItemExposureCodecNormalizesRuntimeBrokerPaths()
{
    var serialized = ItemExposureDefinitionCodec.SerializeDefinitions(
    [
        new ItemExposureDefinition
        {
            ItemPath = "device.mask",
            Format = "b4",
            ExposeBits = true,
            BitCount = 4
        }
    ]);

    var upserted = ItemExposureDefinitionCodec.UpsertDefinition(
        serialized,
        "runtime.item_broker.broker1.client1.device.mask",
        new ItemExposureDefinition
        {
            ItemPath = "broker1.client1.device.mask",
            Format = "b8",
            ExposeBits = true,
            BitCount = 8
        });

    var parsed = ItemExposureDefinitionCodec.ParseDefinitions(upserted);
    AssertEqual(1, parsed.Count);
    AssertEqual("broker1.client1.device.mask", parsed[0].ItemPath);
    AssertEqual("b8", parsed[0].Format);
}

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false.");
    }
}

static void AssertEqual(object? expected, object? actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
    }
}

static void UdlClientFileCodecDerivesIdFromFileName()
{
    var folderDirectory = CreateTemporaryDirectory();

    try
    {
        var clientsDirectory = UdlClientDefinitionFileCodec.GetClientsDirectory(folderDirectory);
        Directory.CreateDirectory(clientsDirectory);

        var filePath = Path.Combine(clientsDirectory, "alpha_client.yaml");
        File.WriteAllText(filePath, """
Text: Alpha client
Host: 10.0.0.15
Port: 9100
AutoConnect: true
DebugLogging: true
Enabled: false
DemoEnabled: true
AttachedItemPaths:
  - module_a.channel_1
DemoModules:
  - Name: module_a
    Kind: Dynamic
""");

        var codec = new UdlClientDefinitionFileCodec();
        var entry = codec.LoadFile(filePath);

        AssertEqual("alpha_client", entry.Definition.ClientId);
        AssertEqual("Alpha client", entry.Definition.Text);
        AssertEqual("10.0.0.15", entry.Definition.Host);
        AssertEqual(9100, entry.Definition.Port);
        AssertTrue(entry.Definition.AutoConnect);
        AssertTrue(entry.Definition.DebugLogging);
        AssertFalse(entry.Definition.Enabled);
        AssertTrue(entry.Definition.DemoEnabled);
        AssertEqual(1, entry.Definition.AttachedItemPaths.Count);
        AssertEqual("module_a.channel_1", entry.Definition.AttachedItemPaths[0]);
        AssertEqual(1, UdlDemoModuleDefinitionCodec.ParseDefinitions(entry.Definition.DemoModuleDefinitions).Count);
    }
    finally
    {
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void DemoSampleKeepsUdlDefinitionFileBacked()
{
    var repositoryRoot = FindRepositoryRoot();
    var folderDirectory = Path.Combine(repositoryRoot, "samples", "HornetStudioDemo", "Folders", "main");
    var folderYamlPath = Path.Combine(folderDirectory, "Folder.yaml");

    var folderYaml = File.ReadAllText(folderYamlPath);
    AssertFalse(folderYaml.Contains("useJitter:", StringComparison.Ordinal));
    AssertFalse(folderYaml.Contains("useSine:", StringComparison.Ordinal));
    AssertFalse(folderYaml.Contains("usePeak:", StringComparison.Ordinal));
    AssertFalse(folderYaml.Contains("noiseMode:", StringComparison.Ordinal));
    AssertFalse(folderYaml.Contains("updateIntervalMs:", StringComparison.Ordinal));
    AssertFalse(folderYaml.Contains("intervalSeconds:", StringComparison.Ordinal));
    AssertFalse(folderYaml.Contains("durationMs:", StringComparison.Ordinal));

    var codec = new UdlClientDefinitionFileCodec();
    var entries = codec.LoadFolder(folderDirectory);
    AssertEqual(1, entries.Count);

    var definition = entries[0].Definition;
    AssertEqual("udl1", definition.ClientId);
    AssertEqual("Udl1", definition.Text);
    AssertEqual("127.0.0.1", definition.Host);
    AssertEqual(9001, definition.Port);
    AssertTrue(definition.AutoConnect);
    AssertTrue(definition.Enabled);
    AssertTrue(definition.DemoEnabled);
    AssertEqual(1, definition.AttachedItemPaths.Count);
    AssertEqual("m001", definition.AttachedItemPaths[0]);
    AssertEqual(1, UdlDemoModuleDefinitionCodec.ParseDefinitions(definition.DemoModuleDefinitions).Count);
}

static void ClientsBrowserEnumeratesUdlFilesFromFolderLocalClientsDirectory()
{
    var folderDirectory = CreateTemporaryDirectory();

    try
    {
        var clientsDirectory = UdlClientDefinitionFileCodec.GetClientsDirectory(folderDirectory);
        Directory.CreateDirectory(clientsDirectory);

        File.WriteAllText(Path.Combine(clientsDirectory, "alpha.yaml"), "Text: Alpha\nHost: 127.0.0.1\nPort: 9001\n");
        File.WriteAllText(Path.Combine(clientsDirectory, "beta.yaml"), "Text: Beta\nHost: 127.0.0.2\nPort: 9002\nDemoEnabled: true\n");

        Directory.CreateDirectory(Path.Combine(folderDirectory, "Clients", "Item"));
        File.WriteAllText(Path.Combine(folderDirectory, "Clients", "Item", "ignored.yaml"), "Text: Ignored\n");

        var codec = new UdlClientDefinitionFileCodec();
        var entries = codec.LoadFolder(folderDirectory);

        AssertEqual(2, entries.Count);
        AssertEqual("alpha", entries[0].Definition.ClientId);
        AssertEqual("beta", entries[1].Definition.ClientId);
    }
    finally
    {
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void ClientsBrowserEntryMapsUdlListFields()
{
    var definition = new UdlClientDefinition
    {
        ClientId = "demo_client",
        Text = "Demo Client",
        Host = "192.168.0.10",
        Port = 9010,
        DemoEnabled = true,
        Enabled = true
    };

    var entry = ClientBrowserEntry.FromUdl(
        new UdlClientFileEntry("B:/demo/Clients/Udl/demo_client.yaml", definition, new UdlClientDefinitionDocument()),
        isConnected: true);

    AssertEqual("demo_client", entry.ClientId);
    AssertEqual("Demo Client", entry.Text);
    AssertEqual("UDL Demo", entry.ClientType);
    AssertEqual("Connected", entry.StatusText);
    AssertEqual("192.168.0.10:9010", entry.SocketText);
    AssertEqual("B:/demo/Clients/Udl/demo_client.yaml", entry.DefinitionFilePath);
}

static void UdlClientFileCodecRoundtripsAndRenames()
{
    var folderDirectory = CreateTemporaryDirectory();

    try
    {
        var codec = new UdlClientDefinitionFileCodec();
        var definition = new UdlClientDefinition
        {
            ClientId = "client_one",
            Text = "Client one",
            Host = "127.0.0.1",
            Port = 9005,
            AutoConnect = true,
            DebugLogging = true,
            Enabled = true,
            DemoEnabled = true,
            AttachedItemPaths = ["module_a.channel_1", "module_b.channel_2"],
            DemoModuleDefinitions = UdlDemoModuleDefinitionCodec.SerializeDefinitions(
            [
                new UdlDemoModuleDefinition
                {
                    Name = "module_a",
                    Kind = UdlDemoModuleKind.Dynamic
                }
            ])
        };

        var originalPath = codec.SaveDefinition(folderDirectory, definition);
        var yaml = File.ReadAllText(originalPath);
        AssertTrue(yaml.Contains("Text: Client one", StringComparison.Ordinal));
        AssertFalse(yaml.Contains("Name:", StringComparison.Ordinal));

        var renamedDefinition = definition with { ClientId = "client_two", Text = "Client two" };
        var renamedPath = codec.SaveDefinition(folderDirectory, renamedDefinition, originalPath);

        AssertFalse(File.Exists(originalPath));
        AssertTrue(File.Exists(renamedPath));

        var reloaded = codec.LoadFile(renamedPath);
        AssertEqual("client_two", reloaded.Definition.ClientId);
        AssertEqual("Client two", reloaded.Definition.Text);
        AssertEqual(2, reloaded.Definition.AttachedItemPaths.Count);
        AssertEqual("module_a.channel_1", reloaded.Definition.AttachedItemPaths[0]);
        AssertEqual("module_b.channel_2", reloaded.Definition.AttachedItemPaths[1]);
        AssertEqual(1, UdlDemoModuleDefinitionCodec.ParseDefinitions(reloaded.Definition.DemoModuleDefinitions).Count);
    }
    finally
    {
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void UdlClientFileCodecRoundtripsModuleExposures()
{
    var folderDirectory = CreateTemporaryDirectory();

    try
    {
        var codec = new UdlClientDefinitionFileCodec();
        var definition = new UdlClientDefinition
        {
            ClientId = "client_with_exposure",
            Text = "Client with exposure",
            Host = "127.0.0.1",
            Port = 9005,
            Enabled = true,
            AttachedItemPaths = ["m001"],
            UdlModuleExposureDefinitions = UdlModuleExposureDefinitionCodec.SerializeDefinitions(
            [
                new UdlModuleExposureDefinition
                {
                    ModuleName = "m001",
                    ChannelName = "read",
                    ExposeBits = true,
                    BitCount = 4,
                    RouteReadInputToSetRequest = true,
                    BitLabels = "Bit0=Ready"
                },
                new UdlModuleExposureDefinition
                {
                    ModuleName = "m001",
                    ChannelName = "alert",
                    ExposeBits = true,
                    BitCount = 2,
                    BitLabels = "Bit1=Fault"
                }
            ])
        };

        var filePath = codec.SaveDefinition(folderDirectory, definition);
        var yaml = File.ReadAllText(filePath);
        AssertTrue(yaml.Contains("UdlModuleExposures:", StringComparison.Ordinal));
        AssertTrue(yaml.Contains("ModuleName: m001", StringComparison.Ordinal));

        var reloaded = codec.LoadFile(filePath).Definition;
        var exposures = UdlModuleExposureDefinitionCodec.ParseDefinitions(reloaded.UdlModuleExposureDefinitions);
        AssertEqual(2, exposures.Count);
        AssertEqual("m001", exposures[0].ModuleName);
        AssertEqual("read", exposures[0].ChannelName);
        AssertTrue(exposures[0].ExposeBits);
        AssertEqual(4, exposures[0].BitCount);
        AssertTrue(exposures[0].RouteReadInputToSetRequest);
        AssertEqual("Bit0=Ready", exposures[0].BitLabels);
        AssertEqual("alert", exposures[1].ChannelName);
        AssertTrue(exposures[1].ExposeBits);
        AssertEqual(2, exposures[1].BitCount);
        AssertEqual("Bit1=Fault", exposures[1].BitLabels);
        AssertEqual(1, reloaded.AttachedItemPaths.Count);
        AssertEqual("m001", reloaded.AttachedItemPaths[0]);
    }
    finally
    {
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void UdlClientFileCodecRoundtripsRouteOnlyModuleExposure()
{
    var folderDirectory = CreateTemporaryDirectory();

    try
    {
        var codec = new UdlClientDefinitionFileCodec();
        var definition = new UdlClientDefinition
        {
            ClientId = "client_with_route_only_exposure",
            Text = "Client with route-only exposure",
            Host = "127.0.0.1",
            Port = 9005,
            Enabled = true,
            AttachedItemPaths = ["m001"],
            UdlModuleExposureDefinitions = UdlModuleExposureDefinitionCodec.SerializeDefinitions(
            [
                new UdlModuleExposureDefinition
                {
                    ModuleName = "m001",
                    ChannelName = "read",
                    RouteReadInputToSetRequest = true
                }
            ])
        };

        var filePath = codec.SaveDefinition(folderDirectory, definition);
        var reloaded = codec.LoadFile(filePath).Definition;
        var exposures = UdlModuleExposureDefinitionCodec.ParseDefinitions(reloaded.UdlModuleExposureDefinitions);
        AssertEqual(1, exposures.Count);
        AssertEqual("m001", exposures[0].ModuleName);
        AssertEqual("read", exposures[0].ChannelName);
        AssertTrue(exposures[0].RouteReadInputToSetRequest);
        AssertFalse(exposures[0].ExposeBits);
        AssertEqual(0, exposures[0].BitCount);
    }
    finally
    {
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void UdlModuleExposureDialogAddsFallbackBitmaskRowsForModuleScope()
{
    var dialog = new UdlModuleExposureDialogWindow(
        ownerViewModel: new MainWindowViewModel(),
        definitions: [],
        runtimeChannels: [],
        moduleName: "m001");

    var bitmaskRows = GetBitmaskRows(dialog);

    AssertEqual(3, bitmaskRows.Count);
    AssertEqual("Alert", bitmaskRows[0].Label);
    AssertEqual("Read", bitmaskRows[1].Label);
    AssertEqual("Set", bitmaskRows[2].Label);
    AssertEqual("4", bitmaskRows[0].BitCountText);
    AssertEqual("4", bitmaskRows[1].BitCountText);
    AssertEqual("4", bitmaskRows[2].BitCountText);
    AssertFalse(bitmaskRows[0].ExposeBits);
    AssertFalse(bitmaskRows[1].ExposeBits);
    AssertFalse(bitmaskRows[2].ExposeBits);
    AssertEqual(0, UdlModuleExposureDefinitionCodec.ParseDefinitions(BuildModuleExposureDialogResult(dialog)).Count);
}

static void UdlModuleExposureDialogSavesFallbackPublishBitsWithoutManualCountEdit()
{
    var dialog = new UdlModuleExposureDialogWindow(
        ownerViewModel: new MainWindowViewModel(),
        definitions: [],
        runtimeChannels: [],
        moduleName: "m001");

    var readRow = GetBitmaskRows(dialog).Single(static row => string.Equals(row.Label, "Read", StringComparison.Ordinal));
    AssertEqual("4", readRow.BitCountText);

    readRow.ExposeBits = true;

    var parsed = UdlModuleExposureDefinitionCodec.ParseDefinitions(BuildModuleExposureDialogResult(dialog));
    AssertEqual(1, parsed.Count);
    AssertEqual("read", parsed[0].ChannelName);
    AssertTrue(parsed[0].ExposeBits);
    AssertEqual(4, parsed[0].BitCount);
}

static void UdlModuleExposureDialogSavesRouteOnlyReadHelperSetting()
{
    var dialog = new UdlModuleExposureDialogWindow(
        ownerViewModel: new MainWindowViewModel(),
        definitions: [],
        runtimeChannels: [],
        moduleName: "m001");

    SetReadInputRouteToSetRequest(dialog, value: true);

    var parsed = UdlModuleExposureDefinitionCodec.ParseDefinitions(BuildModuleExposureDialogResult(dialog));
    AssertEqual(1, parsed.Count);
    AssertEqual("m001", parsed[0].ModuleName);
    AssertEqual("read", parsed[0].ChannelName);
    AssertTrue(parsed[0].RouteReadInputToSetRequest);
    AssertFalse(parsed[0].ExposeBits);
    AssertEqual(0, parsed[0].BitCount);
}

static void UdlModuleExposureDialogReopensSavedRouteOnlyReadHelperSetting()
{
    var dialog = new UdlModuleExposureDialogWindow(
        ownerViewModel: new MainWindowViewModel(),
        definitions:
        [
            new UdlModuleExposureDefinition
            {
                ModuleName = "m001",
                ChannelName = "read",
                RouteReadInputToSetRequest = true
            }
        ],
        runtimeChannels: [],
        moduleName: "m001");

    AssertTrue(GetReadInputRouteToSetRequest(dialog));
}

static void UdlModuleExposureDialogPreservesSavedFallbackBitSettings()
{
    var dialog = new UdlModuleExposureDialogWindow(
        ownerViewModel: new MainWindowViewModel(),
        definitions:
        [
            new UdlModuleExposureDefinition
            {
                ModuleName = "m001",
                ChannelName = "read",
                ExposeBits = true,
                BitCount = 8,
                BitLabels = "Bit0=Ready"
            }
        ],
        runtimeChannels: [],
        moduleName: "m001");

    var readRow = GetBitmaskRows(dialog).Single(static row => string.Equals(row.Label, "Read", StringComparison.Ordinal));
    AssertTrue(readRow.ExposeBits);
    AssertEqual("8", readRow.BitCountText);

    var alertRow = GetBitmaskRows(dialog).Single(static row => string.Equals(row.Label, "Alert", StringComparison.Ordinal));
    alertRow.ExposeBits = true;
    alertRow.BitCountText = "2";

    var parsed = UdlModuleExposureDefinitionCodec.ParseDefinitions(BuildModuleExposureDialogResult(dialog));
    AssertEqual(2, parsed.Count);

    var savedRead = parsed.Single(static definition => string.Equals(definition.ChannelName, "read", StringComparison.Ordinal));
    AssertTrue(savedRead.ExposeBits);
    AssertEqual(8, savedRead.BitCount);
    AssertEqual("Bit0=Ready", savedRead.BitLabels);

    var savedAlert = parsed.Single(static definition => string.Equals(definition.ChannelName, "alert", StringComparison.Ordinal));
    AssertTrue(savedAlert.ExposeBits);
    AssertEqual(2, savedAlert.BitCount);

    AssertFalse(parsed.Any(static definition => string.Equals(definition.ChannelName, "set", StringComparison.Ordinal)));
}

static void UdlModuleExposureDialogKeepsRuntimeBitFormatPrecedence()
{
    var dialog = new UdlModuleExposureDialogWindow(
        ownerViewModel: new MainWindowViewModel(),
        definitions: [],
        runtimeChannels:
        [
            new UdlRuntimeModuleChannelDescriptor
            {
                ModuleName = "m001",
                ChannelName = "read",
                Format = "b16",
                Unit = "raw",
                BitCount = 16
            }
        ],
        moduleName: "m001");

    var bitmaskRows = GetBitmaskRows(dialog);
    AssertEqual(3, bitmaskRows.Count);

    var readRow = bitmaskRows.Single(static row => string.Equals(row.Label, "Read", StringComparison.Ordinal));
    AssertEqual("16", readRow.BitCountText);
    readRow.ExposeBits = true;

    var parsed = UdlModuleExposureDefinitionCodec.ParseDefinitions(BuildModuleExposureDialogResult(dialog));
    var savedRead = parsed.Single(static definition => string.Equals(definition.ChannelName, "read", StringComparison.Ordinal));
    AssertEqual("b16", savedRead.Format);
    AssertEqual(16, savedRead.BitCount);
}

static void UdlRuntimeManagerConnectsSimulatedClient()
{
    var managerType = GetUdlRuntimeManagerType();
    var connectMethod = managerType.GetMethod("ConnectAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.ConnectAsync was not found.");
    var disconnectMethod = managerType.GetMethod("DisconnectAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.DisconnectAsync was not found.");
    var tryGetRuntimeMethod = managerType.GetMethod("TryGetRuntime", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime was not found.");

    var definition = new UdlClientDefinition
    {
        ClientId = "client_demo",
        Host = "127.0.0.1",
        Port = 9001,
        Enabled = true,
        DemoEnabled = true
    };

    var connectTask = (Task?)connectMethod.Invoke(null, ["folder_demo", definition, CancellationToken.None])
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.ConnectAsync returned null.");
    connectTask.GetAwaiter().GetResult();

    var tryGetArgs = new object?[] { "folder_demo", "client_demo", null };
    var found = (bool)(tryGetRuntimeMethod.Invoke(null, tryGetArgs)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime returned null."));
    AssertTrue(found);
    AssertTrue(tryGetArgs[2] is not null);

    var disconnectTask = (Task?)disconnectMethod.Invoke(null, ["folder_demo", "client_demo", CancellationToken.None])
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.DisconnectAsync returned null.");
    disconnectTask.GetAwaiter().GetResult();

    tryGetArgs = ["folder_demo", "client_demo", null];
    found = (bool)(tryGetRuntimeMethod.Invoke(null, tryGetArgs)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime returned null."));
    AssertFalse(found);
}

static IReadOnlyList<UdlModuleExposureBitmaskRow> GetBitmaskRows(UdlModuleExposureDialogWindow dialog)
{
    var dataContext = dialog.DataContext ?? throw new InvalidOperationException("Dialog DataContext was not set.");
    var property = dataContext.GetType().GetProperty("BitmaskRows", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("BitmaskRows property was not found.");

    return ((IEnumerable<UdlModuleExposureBitmaskRow>?)property.GetValue(dataContext))?.ToArray()
        ?? throw new InvalidOperationException("BitmaskRows value was not available.");
}

static string BuildModuleExposureDialogResult(UdlModuleExposureDialogWindow dialog)
{
    var dataContext = dialog.DataContext ?? throw new InvalidOperationException("Dialog DataContext was not set.");
    var method = dataContext.GetType().GetMethod("TryBuildResult", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("TryBuildResult method was not found.");
    object?[] arguments = [null];

    var success = (bool?)method.Invoke(dataContext, arguments)
        ?? throw new InvalidOperationException("TryBuildResult returned no result.");
    AssertTrue(success);

    return arguments[0]?.ToString() ?? string.Empty;
}

static bool GetReadInputRouteToSetRequest(UdlModuleExposureDialogWindow dialog)
{
    var dataContext = dialog.DataContext ?? throw new InvalidOperationException("Dialog DataContext was not set.");
    var property = dataContext.GetType().GetProperty("ReadInputRouteToSetRequest", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("ReadInputRouteToSetRequest property was not found.");

    return (bool?)property.GetValue(dataContext)
        ?? throw new InvalidOperationException("ReadInputRouteToSetRequest value was not available.");
}

static void SetReadInputRouteToSetRequest(UdlModuleExposureDialogWindow dialog, bool value)
{
    var dataContext = dialog.DataContext ?? throw new InvalidOperationException("Dialog DataContext was not set.");
    var property = dataContext.GetType().GetProperty("ReadInputRouteToSetRequest", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("ReadInputRouteToSetRequest property was not found.");

    property.SetValue(dataContext, value);
}

static void UdlFileSaveAndSyncUpdatesFolderRuntimeDefinitions()
{
    const string folderName = "browser_sync_folder";
    var folderDirectory = CreateTemporaryDirectory();

    try
    {
        var codec = new UdlClientDefinitionFileCodec();
        var definition = new UdlClientDefinition
        {
            ClientId = "browser_sync_client",
            Text = "Browser sync client",
            Host = "127.0.0.1",
            Port = 9001,
            AutoConnect = true,
            Enabled = true,
            DemoEnabled = true,
            AttachedItemPaths = ["m001"]
        };

        var filePath = codec.SaveDefinition(folderDirectory, definition);
        InvokeUdlRuntimeManagerSyncDefinitions(folderName, codec.LoadFolder(folderDirectory).Select(static entry => entry.Definition).ToArray(), forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => InvokeUdlRuntimeManagerTryGetRuntime(folderName, definition.ClientId),
            TimeSpan.FromSeconds(5)));

        var updatedDefinition = definition with
        {
            Text = "Browser sync client updated",
            Host = "10.0.0.42",
            AutoConnect = false,
            Enabled = false
        };

        codec.SaveDefinition(folderDirectory, updatedDefinition, filePath);
        InvokeUdlRuntimeManagerSyncDefinitions(folderName, codec.LoadFolder(folderDirectory).Select(static entry => entry.Definition).ToArray(), forceRecreate: false);

        AssertFalse(InvokeUdlRuntimeManagerTryGetRuntime(folderName, definition.ClientId));

        var reloaded = codec.LoadFolder(folderDirectory).Single().Definition;
        AssertEqual("Browser sync client updated", reloaded.Text);
        AssertEqual("10.0.0.42", reloaded.Host);
        AssertFalse(reloaded.AutoConnect);
        AssertFalse(reloaded.Enabled);
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        Directory.Delete(folderDirectory, recursive: true);
    }
}

static void UdlRuntimeManagerProjectsStatusWithoutWidget()
{
    const string folderName = "headless_status_folder";
    const string clientId = "client_headless";
    var endpointPath = $"studio.{folderName}.{clientId}.status.endpoint";
    var connectionPath = $"studio.{folderName}.{clientId}.status.connection";
    var itemCountPath = $"studio.{folderName}.{clientId}.status.item_count";

    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: ["m001"])
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(connectionPath, out var connection)
                && string.Equals(connection?.Value?.ToString(), "Connected", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(5)));
        AssertTrue(HostRegistries.Data.TryResolve(endpointPath, out var endpoint));
        AssertEqual("127.0.0.1:9001", endpoint?.Value?.ToString());
        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(itemCountPath, out var itemCount)
                && Convert.ToInt32(itemCount?.Value ?? 0) >= 1,
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
    }
}

static void UdlRuntimeManagerPublishesAttachedItemsWithoutWidget()
{
    const string folderName = "headless_attach_folder";
    const string clientId = "client_headless";
    var attachedPath = $"studio.{folderName}.{clientId}.m001";

    HostRegistries.Data.Remove(attachedPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: ["m001"])
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(attachedPath, out _),
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeManagerDuplicateAutoConnectSyncDoesNotRecreateAttachedRoots()
{
    const string folderName = "headless_attach_pending_connect_folder";
    const string clientId = "client_headless";
    var attachedPath = $"studio.{folderName}.{clientId}.m001";

    HostRegistries.Data.Remove(attachedPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    var addedRoots = new List<string>();
    void OnRegistryChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind == DataChangeKind.SnapshotUpserted
            && string.Equals(e.Key, attachedPath, StringComparison.OrdinalIgnoreCase))
        {
            addedRoots.Add(e.Key);
        }
    }

    HostRegistries.Data.RegistryChanged += OnRegistryChanged;

    try
    {
        var definitions = new[]
        {
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: ["m001"])
        };

        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: false);
        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: false);
        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: false);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(attachedPath, out _)
                && InvokeUdlRuntimeManagerTryGetRuntime(folderName, clientId),
            TimeSpan.FromSeconds(5)));
        AssertEqual(1, addedRoots.Count);
        AssertFalse(SpinWait.SpinUntil(() => addedRoots.Count > 1, TimeSpan.FromSeconds(1)));
    }
    finally
    {
        HostRegistries.Data.RegistryChanged -= OnRegistryChanged;
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeManagerRefreshDoesNotRecreateAttachedRoots()
{
    const string folderName = "headless_attach_refresh_folder";
    const string clientId = "client_headless";
    var attachedPath = $"studio.{folderName}.{clientId}.m001";

    HostRegistries.Data.Remove(attachedPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    var addedRoots = new List<string>();
    void OnRegistryChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeKind == DataChangeKind.SnapshotUpserted
            && string.Equals(e.Key, attachedPath, StringComparison.OrdinalIgnoreCase))
        {
            addedRoots.Add(e.Key);
        }
    }

    HostRegistries.Data.RegistryChanged += OnRegistryChanged;

    try
    {
        var definitions = new[]
        {
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: ["m001"])
        };

        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(attachedPath, out _)
                && InvokeUdlRuntimeManagerTryGetRuntime(folderName, clientId),
            TimeSpan.FromSeconds(5)));

        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: false);
        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: false);
        InvokeUdlRuntimeManagerSyncDefinitions(folderName, definitions, forceRecreate: false);

        AssertEqual(1, addedRoots.Count);
        AssertFalse(SpinWait.SpinUntil(() => addedRoots.Count > 1, TimeSpan.FromSeconds(1)));
    }
    finally
    {
        HostRegistries.Data.RegistryChanged -= OnRegistryChanged;
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeManagerRemovesLegacyExposureRoot()
{
    const string folderName = "headless_legacy_exposure_folder";
    const string clientId = "client_headless";
    var legacyExposurePath = $"studio.{folderName}.UdlClientruntime.{clientId}.Modules";
    var bitPath = $"studio.{folderName}.{clientId}.m001.read.bits.bit0";

    HostRegistries.Data.Remove(legacyExposurePath);
    HostRegistries.Data.Remove(bitPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "read",
                        ExposeBits = true,
                        BitCount = 1
                    }
                ]))
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(bitPath, out _),
            TimeSpan.FromSeconds(5)));
        AssertFalse(HostRegistries.Data.TryResolve(legacyExposurePath, out _));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(legacyExposurePath);
        HostRegistries.Data.Remove(bitPath);
    }
}

static void UdlRuntimeManagerPublishesExposedBitsWithoutWidget()
{
    const string folderName = "headless_exposure_folder";
    const string clientId = "client_headless";
    var bitPath = $"studio.{folderName}.{clientId}.m001.read.bits.bit0";

    HostRegistries.Data.Remove(bitPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "read",
                        ExposeBits = true,
                        BitCount = 4,
                        BitLabels = "Bit0=Ready"
                    }
                ]))
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(bitPath, out var bit)
                && string.Equals(bit?.Properties["title"].Value?.ToString(), "Ready", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(bitPath);
    }
}

static void UdlRuntimeManagerWritesExposedSetBitsWithoutWidget()
{
    const string folderName = "headless_exposure_set_writeback_folder";
    const string clientId = "client_headless";
    var setBitPath = $"studio.{folderName}.{clientId}.m001.set.bits.bit1";
    var setPath = $"studio.{folderName}.{clientId}.m001.set";

    HostRegistries.Data.Remove(setBitPath);
    HostRegistries.Data.Remove(setPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "set",
                        ExposeBits = true,
                        BitCount = 4
                    }
                ]))
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(setBitPath, out var bit)
                && bit is not null,
            TimeSpan.FromSeconds(5)));

        AssertTrue(HostRegistries.Data.TryResolve(setBitPath, out var existingBit));
        var updatedBit = existingBit!.Clone();
        updatedBit.Value = true;
        HostRegistries.Data.UpsertSnapshot(setBitPath, updatedBit, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(setPath, out var setItem)
                && setItem is not null
                && Convert.ToInt32(setItem.Properties["write"].Value ?? 0) == 2,
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(setBitPath);
        HostRegistries.Data.Remove(setPath);
    }
}

static void UdlRuntimeManagerUpdatesExposedSetBitsFromNumericMaskWithoutWidget()
{
    const string folderName = "headless_exposure_set_numeric_sync_folder";
    const string clientId = "client_headless";
    var setBitPath = $"studio.{folderName}.{clientId}.m001.set.bits.bit0";
    var setPath = $"studio.{folderName}.{clientId}.m001.set";

    HostRegistries.Data.Remove(setBitPath);
    HostRegistries.Data.Remove(setPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "set",
                        ExposeBits = true,
                        BitCount = 4
                    }
                ]))
        ], forceRecreate: true);

        if (!SpinWait.SpinUntil(
                () => HostRegistries.Data.TryResolve(setBitPath, out var bit)
                    && bit is not null,
                TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException($"Expected bit projection '{setBitPath}' to exist.");
        }

        AssertTrue(HostRegistries.Data.TryResolve(setPath, out var existingSet));
        var updatedSet = existingSet!.Clone();
        updatedSet.Properties["write"].Value = 1d;
        HostRegistries.Data.UpsertSnapshot(setPath, updatedSet, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        if (!SpinWait.SpinUntil(
                () => HostRegistries.Data.TryResolve(setBitPath, out var bit)
                    && bit?.Value is true,
                TimeSpan.FromSeconds(5)))
        {
            HostRegistries.Data.TryResolve(setPath, out var currentSet);
            HostRegistries.Data.TryResolve(setBitPath, out var currentBit);
            var setWrite = currentSet?.Properties["write"].Value?.ToString() ?? "<missing>";
            var setRead = currentSet?.Properties["read"].Value?.ToString() ?? "<missing>";
            var bitValue = currentBit?.Value?.ToString() ?? "<missing>";
            throw new InvalidOperationException(
                $"Expected bit0 true after numeric write. SetWrite={setWrite} SetRead={setRead} BitValue={bitValue}");
        }

        updatedSet = updatedSet.Clone();
        updatedSet.Properties["write"].Value = 0d;
        HostRegistries.Data.UpsertSnapshot(setPath, updatedSet, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        if (!SpinWait.SpinUntil(
                () => HostRegistries.Data.TryResolve(setBitPath, out var bit)
                    && bit?.Value is false,
                TimeSpan.FromSeconds(5)))
        {
            HostRegistries.Data.TryResolve(setPath, out var currentSet);
            HostRegistries.Data.TryResolve(setBitPath, out var currentBit);
            var setWrite = currentSet?.Properties["write"].Value?.ToString() ?? "<missing>";
            var setRead = currentSet?.Properties["read"].Value?.ToString() ?? "<missing>";
            var bitValue = currentBit?.Value?.ToString() ?? "<missing>";
            throw new InvalidOperationException(
                $"Expected bit0 false after numeric clear. SetWrite={setWrite} SetRead={setRead} BitValue={bitValue}");
        }
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(setBitPath);
        HostRegistries.Data.Remove(setPath);
    }
}

static void UdlRuntimeManagerRoutesReadBitWritesToSetWithoutWidget()
{
    const string folderName = "headless_exposure_read_route_writeback_folder";
    const string clientId = "client_headless";
    var readBitPath = $"studio.{folderName}.{clientId}.m001.read.bits.bit1";
    var setPath = $"studio.{folderName}.{clientId}.m001.set";

    HostRegistries.Data.Remove(readBitPath);
    HostRegistries.Data.Remove(setPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "read",
                        ExposeBits = true,
                        BitCount = 4,
                        RouteReadInputToSetRequest = true
                    }
                ]))
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(readBitPath, out var bit)
                && bit is not null,
            TimeSpan.FromSeconds(5)));

        AssertTrue(HostRegistries.Data.TryResolve(readBitPath, out var existingBit));
        var updatedBit = existingBit!.Clone();
        updatedBit.Value = true;
        HostRegistries.Data.UpsertSnapshot(readBitPath, updatedBit, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(setPath, out var setItem)
                && setItem is not null
                && Convert.ToInt32(setItem.Properties["write"].Value ?? 0) == 2,
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(readBitPath);
        HostRegistries.Data.Remove(setPath);
    }
}

static void UdlRuntimeManagerRoutedReadBitsFollowSetMaskWithoutWidget()
{
    const string folderName = "headless_exposure_read_route_numeric_sync_folder";
    const string clientId = "client_headless";
    var readBitPath = $"studio.{folderName}.{clientId}.m001.read.bits.bit0";
    var setPath = $"studio.{folderName}.{clientId}.m001.set";

    HostRegistries.Data.Remove(readBitPath);
    HostRegistries.Data.Remove(setPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "read",
                        ExposeBits = true,
                        BitCount = 4,
                        RouteReadInputToSetRequest = true
                    }
                ]))
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(readBitPath, out var bit)
                && bit is not null,
            TimeSpan.FromSeconds(5)));

        AssertTrue(HostRegistries.Data.TryResolve(setPath, out var existingSet));
        var updatedSet = existingSet!.Clone();
        updatedSet.Properties["write"].Value = 1d;
        HostRegistries.Data.UpsertSnapshot(setPath, updatedSet, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(readBitPath, out var bit)
                && bit?.Value is true,
            TimeSpan.FromSeconds(5)));

        updatedSet = updatedSet.Clone();
        updatedSet.Properties["write"].Value = 0d;
        HostRegistries.Data.UpsertSnapshot(setPath, updatedSet, DataRegistryItemMetadata.PublicData(), pruneMissingMembers: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(readBitPath, out var bit)
                && bit?.Value is false,
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(readBitPath);
        HostRegistries.Data.Remove(setPath);
    }
}

static void UdlRuntimeManagerPublishesBitExposureParityAcrossChannelsWithoutWidget()
{
    const string folderName = "headless_exposure_parity_folder";
    const string clientId = "client_headless";
    var readBitPath = $"studio.{folderName}.{clientId}.m001.read.bits.bit3";
    var setBitPath = $"studio.{folderName}.{clientId}.m001.set.bits.bit2";
    var alertBitPath = $"studio.{folderName}.{clientId}.m001.alert.bits.bit1";
    var missingAlertBitPath = $"studio.{folderName}.{clientId}.m001.alert.bits.bit2";

    HostRegistries.Data.Remove(readBitPath);
    HostRegistries.Data.Remove(setBitPath);
    HostRegistries.Data.Remove(alertBitPath);
    HostRegistries.Data.Remove(missingAlertBitPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(
                clientId,
                attachedItemPaths: ["m001"],
                moduleExposureDefinitions: UdlModuleExposureDefinitionCodec.SerializeDefinitions(
                [
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "read",
                        ExposeBits = true,
                        BitCount = 4,
                        BitLabels = "Bit3=Read Flag"
                    },
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "set",
                        ExposeBits = true,
                        BitCount = 3,
                        BitLabels = "Bit2=Set Flag"
                    },
                    new UdlModuleExposureDefinition
                    {
                        ModuleName = "m001",
                        ChannelName = "alert",
                        ExposeBits = true,
                        BitCount = 2,
                        BitLabels = "Bit1=Alert Flag"
                    }
                ]))
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(readBitPath, out var readBit)
                && string.Equals(readBit?.Properties["title"].Value?.ToString(), "Read Flag", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5)));
        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(setBitPath, out var setBit)
                && string.Equals(setBit?.Properties["title"].Value?.ToString(), "Set Flag", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5)));
        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(alertBitPath, out var alertBit)
                && string.Equals(alertBit?.Properties["title"].Value?.ToString(), "Alert Flag", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5)));
        AssertFalse(HostRegistries.Data.TryResolve(missingAlertBitPath, out _));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(readBitPath);
        HostRegistries.Data.Remove(setBitPath);
        HostRegistries.Data.Remove(alertBitPath);
        HostRegistries.Data.Remove(missingAlertBitPath);
    }
}

static void UdlRuntimeManagerRemovesAttachedProjectionAfterDetachUpdate()
{
    const string folderName = "headless_detach_folder";
    const string clientId = "client_headless";
    var attachedPath = $"studio.{folderName}.{clientId}.m001";

    HostRegistries.Data.Remove(attachedPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: ["m001"])
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(attachedPath, out _),
            TimeSpan.FromSeconds(5)));

        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: Array.Empty<string>())
        ], forceRecreate: false);

        AssertTrue(SpinWait.SpinUntil(
            () => !HostRegistries.Data.TryResolve(attachedPath, out _),
            TimeSpan.FromSeconds(5)));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeManagerKeepsRawItemsPrivateWithoutAttach()
{
    const string folderName = "headless_private_folder";
    const string clientId = "client_headless";
    var attachedPath = $"studio.{folderName}.{clientId}.m001";
    var statusPath = $"studio.{folderName}.{clientId}.status.connection";

    HostRegistries.Data.Remove(attachedPath);
    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: Array.Empty<string>())
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(statusPath, out var connection)
                && string.Equals(connection?.Value?.ToString(), "Connected", StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(5)));
        AssertFalse(HostRegistries.Data.TryResolve(attachedPath, out _));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeManagerReleaseClearsProjectionState()
{
    const string folderName = "headless_release_folder";
    const string clientId = "client_headless";
    var statusPath = $"studio.{folderName}.{clientId}.status.connection";
    var attachedPath = $"studio.{folderName}.{clientId}.m001";

    ReleaseUdlRuntimeManagerFolder(folderName);

    try
    {
        InvokeUdlRuntimeManagerSyncDefinitions(folderName,
        [
            CreateHeadlessUdlDefinition(clientId, attachedItemPaths: ["m001"])
        ], forceRecreate: true);

        AssertTrue(SpinWait.SpinUntil(
            () => HostRegistries.Data.TryResolve(statusPath, out _)
                && HostRegistries.Data.TryResolve(attachedPath, out _),
            TimeSpan.FromSeconds(5)));

        ReleaseUdlRuntimeManagerFolder(folderName);

        AssertFalse(HostRegistries.Data.TryResolve(statusPath, out _));
        AssertFalse(HostRegistries.Data.TryResolve(attachedPath, out _));
    }
    finally
    {
        ReleaseUdlRuntimeManagerFolder(folderName);
        HostRegistries.Data.Remove(statusPath);
        HostRegistries.Data.Remove(attachedPath);
    }
}

static void UdlRuntimeManagerReleaseFolderClearsConnectedRuntimes()
{
    var managerType = GetUdlRuntimeManagerType();
    var connectMethod = managerType.GetMethod("ConnectAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.ConnectAsync was not found.");
    var releaseFolderMethod = managerType.GetMethod("ReleaseFolder", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.ReleaseFolder was not found.");
    var tryGetRuntimeMethod = managerType.GetMethod("TryGetRuntime", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime was not found.");

    foreach (var clientId in new[] { "client_demo_1", "client_demo_2" })
    {
        var definition = new UdlClientDefinition
        {
            ClientId = clientId,
            Host = "127.0.0.1",
            Port = 9001,
            Enabled = true,
            DemoEnabled = true
        };

        var connectTask = (Task?)connectMethod.Invoke(null, ["folder_release_demo", definition, CancellationToken.None])
            ?? throw new InvalidOperationException("UdlClientRuntimeManager.ConnectAsync returned null.");
        connectTask.GetAwaiter().GetResult();
    }

    releaseFolderMethod.Invoke(null, ["folder_release_demo"]);

    foreach (var clientId in new[] { "client_demo_1", "client_demo_2" })
    {
        var tryGetArgs = new object?[] { "folder_release_demo", clientId, null };
        var found = (bool)(tryGetRuntimeMethod.Invoke(null, tryGetArgs)
            ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime returned null."));
        AssertFalse(found);
    }
}

static string CreateTemporaryDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "HornetStudioEditorTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var solutionPath = Path.Combine(directory.FullName, "HornetStudio.sln");
        if (File.Exists(solutionPath))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root from the editor test host.");
}

static Type GetUdlRuntimeManagerType()
{
    return typeof(UdlClientControl).Assembly.GetType("HornetStudio.Editor.UdlClients.UdlClientRuntimeManager")
        ?? throw new InvalidOperationException("UdlClientRuntimeManager type was not found.");
}

static void InvokeUdlRuntimeManagerSyncDefinitions(string folderName, IReadOnlyList<UdlClientDefinition> definitions, bool forceRecreate)
{
    var syncMethod = GetUdlRuntimeManagerType().GetMethod("SyncDefinitions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.SyncDefinitions was not found.");

    syncMethod.Invoke(null, [folderName, definitions, forceRecreate]);
}

static bool InvokeUdlRuntimeManagerTryGetRuntime(string folderName, string clientId)
{
    var tryGetRuntimeMethod = GetUdlRuntimeManagerType().GetMethod("TryGetRuntime", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime was not found.");

    var tryGetArgs = new object?[] { folderName, clientId, null };
    return (bool)(tryGetRuntimeMethod.Invoke(null, tryGetArgs)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.TryGetRuntime returned null."));
}

static void ReleaseUdlRuntimeManagerFolder(string folderName)
{
    var releaseFolderMethod = GetUdlRuntimeManagerType().GetMethod("ReleaseFolder", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UdlClientRuntimeManager.ReleaseFolder was not found.");

    releaseFolderMethod.Invoke(null, [folderName]);
}

static UdlClientDefinition CreateHeadlessUdlDefinition(string clientId, IReadOnlyList<string> attachedItemPaths, string? moduleExposureDefinitions = null)
{
    return new UdlClientDefinition
    {
        ClientId = clientId,
        Host = "127.0.0.1",
        Port = 9001,
        AutoConnect = true,
        Enabled = true,
        DemoEnabled = true,
        AttachedItemPaths = attachedItemPaths,
        UdlModuleExposureDefinitions = moduleExposureDefinitions ?? string.Empty,
        DemoModuleDefinitions = UdlDemoModuleDefinitionCodec.SerializeDefinitions(
        [
            new UdlDemoModuleDefinition
            {
                Name = "m001",
                Kind = UdlDemoModuleKind.SetDriven,
                InitialValue = 0
            }
        ])
    };
}

static EditorDialogField CreateInteractionRuleField()
{
    var definition = new EditorDialogBindingDefinition(
        "InteractionRules",
        "Interaction Rules",
        EditorPropertyType.InteractionRuleList,
        static _ => string.Empty);
    var item = new FolderItemModel
    {
        Kind = ControlKind.Button
    };

    return definition.CreateField(item);
}

static string? GetPrivateConstString(Type type, string fieldName)
{
    var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
    if (field is null)
    {
        throw new InvalidOperationException($"Field '{fieldName}' was not found on '{type.Name}'.");
    }

    return field.GetRawConstantValue()?.ToString();
}

static void SetUiDiagnosticsEnabledForTest(bool enabled)
{
    var field = typeof(UiResponsivenessDiagnostics).GetField("_isEnabled", BindingFlags.NonPublic | BindingFlags.Static);
    if (field is null)
    {
        throw new InvalidOperationException("UiResponsivenessDiagnostics._isEnabled was not found.");
    }

    field.SetValue(null, enabled ? 1 : 0);
}

static void ResetUiDiagnosticsBacklogCounters()
{
    foreach (var fieldName in new[]
    {
        "_activeTargetRefreshQueuedCount",
        "_activeTargetRefreshFollowUpCount",
        "_activeTargetValueRefreshQueuedCount",
        "_activeUnresolvedSignalTargetCount"
    })
    {
        var field = typeof(UiResponsivenessDiagnostics).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        if (field is null)
        {
            throw new InvalidOperationException($"UiResponsivenessDiagnostics.{fieldName} was not found.");
        }

        field.SetValue(null, 0);
    }
}

static object CaptureUiDiagnosticsBacklogSnapshot()
{
    var method = typeof(UiResponsivenessDiagnostics).GetMethod(
        "CaptureSignalRefreshBacklogSnapshot",
        BindingFlags.NonPublic | BindingFlags.Static);
    if (method is null)
    {
        throw new InvalidOperationException("CaptureSignalRefreshBacklogSnapshot was not found.");
    }

    return method.Invoke(null, null)
        ?? throw new InvalidOperationException("CaptureSignalRefreshBacklogSnapshot returned null.");
}

static object CreateUiDiagnosticsBacklogSnapshot(
    int targetRefreshQueued,
    int targetRefreshFollowUpQueued,
    int targetValueRefreshQueued,
    int unresolvedTargetGateCount)
{
    var type = GetUiDiagnosticsNestedType("SignalRefreshBacklogSnapshot");
    var constructor = type.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(int), typeof(int), typeof(int), typeof(int)],
        modifiers: null);
    if (constructor is null)
    {
        throw new InvalidOperationException("SignalRefreshBacklogSnapshot constructor was not found.");
    }

    return constructor.Invoke([targetRefreshQueued, targetRefreshFollowUpQueued, targetValueRefreshQueued, unresolvedTargetGateCount]);
}

static object CreateUiBenchmarkSession(string scenario, TimeSpan configuredDuration, TimeSpan warmupDuration)
{
    var type = GetUiDiagnosticsNestedType("UiBenchmarkSession");
    var constructor = type.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(string), typeof(TimeSpan), typeof(TimeSpan)],
        modifiers: null);
    if (constructor is null)
    {
        throw new InvalidOperationException("UiBenchmarkSession constructor was not found.");
    }

    return constructor.Invoke([scenario, configuredDuration, warmupDuration]);
}

static object? InvokeUiBenchmarkSessionMethod(object session, string methodName, params object?[]? args)
{
    var method = session.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException($"UiBenchmarkSession.{methodName} was not found.");
    }

    return method.Invoke(session, args);
}

static Type GetUiDiagnosticsNestedType(string name)
{
    return typeof(UiResponsivenessDiagnostics).GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"UiResponsivenessDiagnostics nested type '{name}' was not found.");
}

static T GetInstanceProperty<T>(object instance, string propertyName)
{
    var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property is null)
    {
        throw new InvalidOperationException($"Property '{propertyName}' was not found on '{instance.GetType().Name}'.");
    }

    var value = property.GetValue(instance);
    if (value is null)
    {
        throw new InvalidOperationException($"Property '{propertyName}' returned null.");
    }

    return (T)value;
}

static bool ContainsTreePath(IEnumerable<HornetStudio.ViewModels.ItemTreeNodeViewModel> nodes, string path)
{
    foreach (var node in nodes)
    {
        if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase)
            || ContainsTreePath(node.Children, path))
        {
            return true;
        }
    }

    return false;
}

sealed class FakeHostItemBrokerClient : IHostItemBrokerClient
{
    private Action<string>? _diagnostic;
    private Action? _itemsChanged;

    public string ClientIdValue { get; set; } = "broker-widget-test";

    public string Name => "ItemClientTest";

    public string Host => "127.0.0.1";

    public int Port => 1883;

    public string BaseTopic => "hornet";

    public string ClientId => ClientIdValue;

    public bool IsConnected => true;

    public ItemDictionary Items { get; } = new("runtime.item_broker.ItemClientTest");

    public List<FakeItemSubscription> Subscriptions { get; } = [];

    public List<ItemModel> PublishedSnapshots { get; } = [];

    public List<ItemModel> ValueUpdates { get; } = [];

    public List<(ItemModel ItemModel, string ParameterName)> ParameterUpdates { get; } = [];

    public event Action<string>? Diagnostic
    {
        add => _diagnostic += value;
        remove => _diagnostic -= value;
    }

    public event Action? ItemsChanged
    {
        add => _itemsChanged += value;
        remove => _itemsChanged -= value;
    }

    public IReadOnlyDictionary<string, ItemModel> GetItemSnapshots()
        => Items.GetDictionary().ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ItemModel> GetReceivedItemRootSnapshots()
        => GetItemSnapshots();

    public Task PublishSnapshotAsync(ItemModel item, CancellationToken cancellationToken = default)
    {
        PublishedSnapshots.Add(item.Clone());
        return Task.CompletedTask;
    }

    public Task PublishReadAsync(
        ItemModel item,
        bool publishEpoch = true,
        bool retained = false,
        CancellationToken cancellationToken = default)
    {
        ValueUpdates.Add(item.Clone());
        return Task.CompletedTask;
    }

    public Task PublishPropertyAsync(
        ItemModel item,
        string parameterName,
        bool retained = false,
        CancellationToken cancellationToken = default)
    {
        ParameterUpdates.Add((item.Clone(), parameterName));
        return Task.CompletedTask;
    }

    public Task<IItemSubscription> SubscribeAsync(
        string path,
        Func<ItemServerMessage, CancellationToken, Task> handler,
        ItemSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = new FakeItemSubscription(path, options?.Recursive ?? true, handler);
        Subscriptions.Add(subscription);
        return Task.FromResult<IItemSubscription>(subscription);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void PublishToSubscription(ItemServerMessage message)
    {
        foreach (var subscription in Subscriptions.Where(subscription => string.Equals(subscription.Path, message.Path, StringComparison.OrdinalIgnoreCase)))
        {
            subscription.HandleAsync(message).GetAwaiter().GetResult();
        }
    }
}

sealed class FakeItemSubscription : IItemSubscription
{
    private readonly Func<ItemServerMessage, CancellationToken, Task> _handler;

    public FakeItemSubscription(string path, bool recursive, Func<ItemServerMessage, CancellationToken, Task> handler)
    {
        Path = path;
        Recursive = recursive;
        _handler = handler;
    }

    public string SubscriptionId { get; } = Guid.NewGuid().ToString("N");

    public string Path { get; }

    public bool Recursive { get; }

    public bool Disposed { get; private set; }

    public Task HandleAsync(ItemServerMessage message)
        => _handler(message, CancellationToken.None);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
