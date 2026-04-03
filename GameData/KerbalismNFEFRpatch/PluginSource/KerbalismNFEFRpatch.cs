using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace KerbalismNFEFRpatch
{
	[KSPAddon(KSPAddon.Startup.EveryScene, false)]
	public class KerbalismNFEFRpatchBridge : MonoBehaviour
	{
		private const string ReactorModuleName = "ModuleSystemHeatFissionReactor";
		private const string ProcessControllerModuleName = "ProcessController";
		private const string ReactorProcessResource = "_Nukereactor";
		private const float LoadedSyncIntervalSeconds = 0.5f;
		private const float ProtoSyncIntervalSeconds = 5.0f;
		private const string LogPrefix = "[KerbalismNFEFRpatch]";

		private readonly Dictionary<uint, double> _maxCapacityByFlightId = new Dictionary<uint, double>();
		private float _nextLoadedSyncAt;
		private float _nextProtoSyncAt;
		private bool _isSceneActive;
		private static bool _loadLogged;

		private static readonly Dictionary<string, double> FallbackMaxCapacityByPartName =
			new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
			{
				{ "nfe-reactor-tiny-1", 0.5 },
				{ "nfe-reactor-tiny-2", 1.5 },
				{ "nfe-reactor-0625-1", 6.0 },
				{ "nfe-reactor-0625", 6.0 },
				{ "nfe-reactor-125-1", 20.0 },
				{ "nfe-reactor-125", 20.0 },
				{ "nfe-reactor-1875-1", 62.5 },
				{ "nfe-reactor-25-1", 200.0 },
				{ "nfe-reactor-25-2", 300.0 },
				{ "nfe-reactor-375-1", 500.0 },
				{ "nfe-reactor-375", 500.0 },
				{ "nfe-reactor-375-2", 600.0 }
			};

		private void Start()
		{
			_isSceneActive = IsSupportedScene();
			if (!_isSceneActive)
			{
				enabled = false;
				return;
			}

			GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
			GameEvents.onVesselWasModified.Add(OnVesselWasModified);
			GameEvents.onVesselChange.Add(OnVesselChange);
			GameEvents.onGameStateSave.Add(OnGameStateSave);

			_nextLoadedSyncAt = 0f;
			_nextProtoSyncAt = 0f;
			SyncAll();
			if (!_loadLogged)
			{
				Debug.Log(LogPrefix + " loaded");
				_loadLogged = true;
			}
		}

		private void OnDestroy()
		{
			if (!_isSceneActive)
			{
				return;
			}

			GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
			GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
			GameEvents.onVesselChange.Remove(OnVesselChange);
			GameEvents.onGameStateSave.Remove(OnGameStateSave);
		}

		private void Update()
		{
			if (!_isSceneActive || !IsGameReady())
			{
				return;
			}

			float now = Time.realtimeSinceStartup;

			if (now >= _nextLoadedSyncAt)
			{
				_nextLoadedSyncAt = now + LoadedSyncIntervalSeconds;
				SyncLoadedVessels();
			}

			if (now >= _nextProtoSyncAt)
			{
				_nextProtoSyncAt = now + ProtoSyncIntervalSeconds;
				SyncProtoVessels();
			}
		}

		private void OnVesselGoOnRails(Vessel vessel)
		{
			SyncLoadedVessel(vessel, true);
			if (vessel != null && vessel.protoVessel != null)
			{
				SyncProtoVessel(vessel.protoVessel);
			}
		}

		private void OnVesselGoOffRails(Vessel vessel)
		{
			SyncLoadedVessel(vessel, false);
		}

		private void OnVesselWasModified(Vessel vessel)
		{
			SyncLoadedVessel(vessel, false);
		}

		private void OnVesselChange(Vessel vessel)
		{
			SyncLoadedVessel(vessel, false);
		}

		private void OnGameStateSave(ConfigNode _)
		{
			SyncAll();
		}

		private bool IsGameReady()
		{
			return HighLogic.CurrentGame != null
				&& HighLogic.CurrentGame.flightState != null;
		}

		private static bool IsSupportedScene()
		{
			return HighLogic.LoadedSceneIsFlight
				|| HighLogic.LoadedScene == GameScenes.TRACKSTATION
				|| HighLogic.LoadedScene == GameScenes.SPACECENTER;
		}

		private void SyncAll()
		{
			if (!IsGameReady())
			{
				return;
			}

			SyncLoadedVessels();
			SyncProtoVessels();
		}

		private void SyncLoadedVessels()
		{
			if (FlightGlobals.VesselsLoaded != null)
			{
				for (int i = 0; i < FlightGlobals.VesselsLoaded.Count; i++)
				{
					SyncLoadedVessel(FlightGlobals.VesselsLoaded[i], false);
				}
			}
		}

		private void SyncLoadedVessel(Vessel vessel, bool enableProcessForBackground)
		{
			if (vessel == null || !vessel.loaded || vessel.parts == null)
			{
				return;
			}

			for (int i = 0; i < vessel.parts.Count; i++)
			{
				Part part = vessel.parts[i];
				if (part == null)
				{
					continue;
				}

				PartModule reactor = FindModule(part, ReactorModuleName);
				PartModule process = FindReactorProcessController(part);
				if (reactor == null || process == null)
				{
					continue;
				}

				bool reactorEnabled = IsLoadedReactorActive(reactor);
				double maxCapacity = GetOrCacheMaxCapacity(part, process, reactor);
				double targetCapacity = reactorEnabled
					? InferLoadedCapacityFromReactor(reactor, maxCapacity)
					: 0.0;
				bool targetRunning = enableProcessForBackground && reactorEnabled;

				ApplyProcessSettings(process, targetRunning, targetCapacity);
				SyncLoadedNukereactorResource(part, targetCapacity, reactorEnabled);
				SyncProtoPartFromLoadedPart(vessel, part, reactorEnabled, targetCapacity, reactorEnabled);
			}
		}

		private void SyncProtoVessels()
		{
			var flightState = HighLogic.CurrentGame.flightState;
			if (flightState == null || flightState.protoVessels == null)
			{
				return;
			}

			for (int i = 0; i < flightState.protoVessels.Count; i++)
			{
				ProtoVessel protoVessel = flightState.protoVessels[i];
				if (protoVessel == null)
				{
					continue;
				}

				if (protoVessel.vesselRef != null && protoVessel.vesselRef.loaded)
				{
					continue;
				}

				SyncProtoVessel(protoVessel);
			}
		}

		private void SyncProtoVessel(ProtoVessel protoVessel)
		{
			if (protoVessel.protoPartSnapshots == null)
			{
				return;
			}

			for (int i = 0; i < protoVessel.protoPartSnapshots.Count; i++)
			{
				ProtoPartSnapshot partSnapshot = protoVessel.protoPartSnapshots[i];
				if (partSnapshot == null || partSnapshot.modules == null)
				{
					continue;
				}

				ProtoPartModuleSnapshot reactorSnapshot = FindProtoModule(partSnapshot, ReactorModuleName);
				if (reactorSnapshot == null)
				{
					continue;
				}

				List<ProtoPartModuleSnapshot> processSnapshots = FindProtoReactorProcessModules(partSnapshot);
				if (processSnapshots.Count == 0)
				{
					continue;
				}

				bool reactorEnabled = IsProtoReactorActive(reactorSnapshot);
				double maxCapacity = GetOrCacheProtoMaxCapacity(partSnapshot, processSnapshots[0], reactorSnapshot);
				double targetCapacity = reactorEnabled
					? InferProtoCapacityFromReactor(reactorSnapshot, maxCapacity)
					: 0.0;

				for (int j = 0; j < processSnapshots.Count; j++)
				{
					ApplyProtoProcessSettings(processSnapshots[j], reactorEnabled, targetCapacity);
				}

				SyncProtoNukereactorResource(partSnapshot, targetCapacity, reactorEnabled);
			}
		}

		private void SyncProtoPartFromLoadedPart(Vessel vessel, Part part, bool reactorEnabled, double targetCapacity, bool targetRunning)
		{
			ProtoPartSnapshot targetSnapshot = part.protoPartSnapshot;
			if (targetSnapshot == null)
			{
				targetSnapshot = FindProtoPartSnapshot(vessel, part.flightID);
			}

			if (targetSnapshot == null || targetSnapshot.modules == null)
			{
				return;
			}

			List<ProtoPartModuleSnapshot> processSnapshots = FindProtoReactorProcessModules(targetSnapshot);
			for (int i = 0; i < processSnapshots.Count; i++)
			{
				ApplyProtoProcessSettings(processSnapshots[i], targetRunning, targetCapacity);
			}

			SyncProtoNukereactorResource(targetSnapshot, targetCapacity, reactorEnabled);
		}

		private static ProtoPartSnapshot FindProtoPartSnapshot(Vessel vessel, uint flightId)
		{
			if (vessel == null || vessel.protoVessel == null || vessel.protoVessel.protoPartSnapshots == null)
			{
				return null;
			}

			for (int i = 0; i < vessel.protoVessel.protoPartSnapshots.Count; i++)
			{
				ProtoPartSnapshot snapshot = vessel.protoVessel.protoPartSnapshots[i];
				if (snapshot != null && snapshot.flightID == flightId)
				{
					return snapshot;
				}
			}

			return null;
		}

		private static void SyncLoadedNukereactorResource(Part part, double targetCapacity, bool reactorEnabled)
		{
			if (part == null || part.Resources == null)
			{
				return;
			}

			double safeCapacity = targetCapacity > 0.0 ? targetCapacity : 0.0;
			double targetAmount = reactorEnabled ? safeCapacity : 0.0;
			for (int i = 0; i < part.Resources.Count; i++)
			{
				PartResource resource = part.Resources[i];
				if (resource == null || !string.Equals(resource.resourceName, ReactorProcessResource, StringComparison.Ordinal))
				{
					continue;
				}

				resource.maxAmount = safeCapacity;
				resource.amount = Clamp(targetAmount, 0.0, safeCapacity);

				break;
			}
		}

		private static void SyncProtoNukereactorResource(ProtoPartSnapshot partSnapshot, double targetCapacity, bool reactorEnabled)
		{
			if (partSnapshot == null || partSnapshot.resources == null)
			{
				return;
			}

			double safeCapacity = targetCapacity > 0.0 ? targetCapacity : 0.0;
			double targetAmount = reactorEnabled ? safeCapacity : 0.0;
			for (int i = 0; i < partSnapshot.resources.Count; i++)
			{
				ProtoPartResourceSnapshot resource = partSnapshot.resources[i];
				if (resource == null || !string.Equals(resource.resourceName, ReactorProcessResource, StringComparison.Ordinal))
				{
					continue;
				}

				resource.maxAmount = safeCapacity;
				resource.amount = Clamp(targetAmount, 0.0, safeCapacity);

				break;
			}
		}

		private static PartModule FindModule(Part part, string moduleName)
		{
			if (part == null || part.Modules == null)
			{
				return null;
			}

			for (int i = 0; i < part.Modules.Count; i++)
			{
				PartModule module = part.Modules[i];
				if (module != null && string.Equals(module.moduleName, moduleName, StringComparison.Ordinal))
				{
					return module;
				}
			}

			return null;
		}

		private static PartModule FindReactorProcessController(Part part)
		{
			if (part == null || part.Modules == null)
			{
				return null;
			}

			for (int i = 0; i < part.Modules.Count; i++)
			{
				PartModule module = part.Modules[i];
				if (module == null || !string.Equals(module.moduleName, ProcessControllerModuleName, StringComparison.Ordinal))
				{
					continue;
				}

				string resource = ReadModuleString(module, "resource");
				if (string.Equals(resource, ReactorProcessResource, StringComparison.Ordinal))
				{
					return module;
				}
			}

			return null;
		}

		private static ProtoPartModuleSnapshot FindProtoModule(ProtoPartSnapshot partSnapshot, string moduleName)
		{
			if (partSnapshot == null || partSnapshot.modules == null)
			{
				return null;
			}

			for (int i = 0; i < partSnapshot.modules.Count; i++)
			{
				ProtoPartModuleSnapshot module = partSnapshot.modules[i];
				if (module != null && string.Equals(module.moduleName, moduleName, StringComparison.Ordinal))
				{
					return module;
				}
			}

			return null;
		}

		private static List<ProtoPartModuleSnapshot> FindProtoReactorProcessModules(ProtoPartSnapshot partSnapshot)
		{
			List<ProtoPartModuleSnapshot> result = new List<ProtoPartModuleSnapshot>();
			List<ProtoPartModuleSnapshot> allProcessControllers = new List<ProtoPartModuleSnapshot>();
			if (partSnapshot == null || partSnapshot.modules == null)
			{
				return result;
			}

			for (int i = 0; i < partSnapshot.modules.Count; i++)
			{
				ProtoPartModuleSnapshot module = partSnapshot.modules[i];
				if (module == null || !string.Equals(module.moduleName, ProcessControllerModuleName, StringComparison.Ordinal))
				{
					continue;
				}

				allProcessControllers.Add(module);
				string resource = ReadProtoString(module, "resource");
				if (string.Equals(resource, ReactorProcessResource, StringComparison.Ordinal))
				{
					result.Add(module);
				}
			}

			if (result.Count > 0)
			{
				return result;
			}

			if (allProcessControllers.Count == 1)
			{
				result.Add(allProcessControllers[0]);
				return result;
			}

			for (int i = 0; i < allProcessControllers.Count; i++)
			{
				string title = ReadProtoString(allProcessControllers[i], "title");
				if (!string.IsNullOrEmpty(title) && title.IndexOf("fission", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					result.Add(allProcessControllers[i]);
				}
			}

			return result;
		}

		private double GetOrCacheMaxCapacity(Part part, PartModule processModule, PartModule reactorModule)
		{
			double cached;
			if (_maxCapacityByFlightId.TryGetValue(part.flightID, out cached) && cached > 0.0)
			{
				return cached;
			}

			double maxCapacity = ReadModuleDouble(processModule, "capacity", 0.0);
			double maxFromGenerator = ReadModuleDouble(reactorModule, "MaxElectricalGeneration", 0.0) / 10.0;

			if (maxFromGenerator > maxCapacity)
			{
				maxCapacity = maxFromGenerator;
			}

			double fromFallback;
			if (FallbackMaxCapacityByPartName.TryGetValue(part.partInfo != null ? part.partInfo.name : part.name, out fromFallback) && fromFallback > maxCapacity)
			{
				maxCapacity = fromFallback;
			}

			if (maxCapacity <= 0.0)
			{
				maxCapacity = 1.0;
			}

			_maxCapacityByFlightId[part.flightID] = maxCapacity;
			return maxCapacity;
		}

		private double GetOrCacheProtoMaxCapacity(ProtoPartSnapshot partSnapshot, ProtoPartModuleSnapshot processSnapshot, ProtoPartModuleSnapshot reactorSnapshot)
		{
			double cached;
			if (_maxCapacityByFlightId.TryGetValue(partSnapshot.flightID, out cached) && cached > 0.0)
			{
				return cached;
			}

			double maxCapacity = ReadProtoDouble(processSnapshot, "capacity", 0.0);
			double maxFromGenerator = ReadProtoDouble(reactorSnapshot, "MaxElectricalGeneration", 0.0) / 10.0;

			if (maxFromGenerator > maxCapacity)
			{
				maxCapacity = maxFromGenerator;
			}

			double fromFallback;
			if (FallbackMaxCapacityByPartName.TryGetValue(partSnapshot.partName, out fromFallback) && fromFallback > maxCapacity)
			{
				maxCapacity = fromFallback;
			}

			if (maxCapacity <= 0.0)
			{
				maxCapacity = 1.0;
			}

			_maxCapacityByFlightId[partSnapshot.flightID] = maxCapacity;
			return maxCapacity;
		}

		private static double InferLoadedCapacityFromReactor(PartModule reactorModule, double maxCapacity)
		{
			double powerPercent = ReadLoadedPowerPercent(reactorModule);
			if (!double.IsNaN(powerPercent) && powerPercent > 0.0)
			{
				return Clamp(maxCapacity * (powerPercent / 100.0), 0.0, maxCapacity);
			}

			double currentGeneration = ReadModuleDouble(reactorModule, "CurrentElectricalGeneration", double.NaN);
			if (!double.IsNaN(currentGeneration) && currentGeneration > 0.0)
			{
				return Clamp(currentGeneration / 10.0, 0.0, maxCapacity);
			}

			return maxCapacity;
		}

		private static double InferProtoCapacityFromReactor(ProtoPartModuleSnapshot reactorSnapshot, double maxCapacity)
		{
			double powerPercent = ReadProtoPowerPercent(reactorSnapshot);
			if (!double.IsNaN(powerPercent) && powerPercent > 0.0)
			{
				return Clamp(maxCapacity * (powerPercent / 100.0), 0.0, maxCapacity);
			}

			double currentGeneration = ReadProtoDouble(reactorSnapshot, "CurrentElectricalGeneration", double.NaN);
			if (!double.IsNaN(currentGeneration) && currentGeneration > 0.0)
			{
				return Clamp(currentGeneration / 10.0, 0.0, maxCapacity);
			}

			return maxCapacity;
		}

		private static bool IsLoadedReactorActive(PartModule reactorModule)
		{
			if (ReadModuleBool(reactorModule, "Enabled", false))
			{
				return true;
			}

			double powerPercent = ReadLoadedPowerPercent(reactorModule);
			if (!double.IsNaN(powerPercent) && powerPercent > 0.1)
			{
				return true;
			}

			double currentGeneration = ReadModuleDouble(reactorModule, "CurrentElectricalGeneration", double.NaN);
			return !double.IsNaN(currentGeneration) && currentGeneration > 0.01;
		}

		private static bool IsProtoReactorActive(ProtoPartModuleSnapshot reactorSnapshot)
		{
			if (ReadProtoBool(reactorSnapshot, "Enabled", false))
			{
				return true;
			}

			double powerPercent = ReadProtoPowerPercent(reactorSnapshot);
			if (!double.IsNaN(powerPercent) && powerPercent > 0.1)
			{
				return true;
			}

			double currentGeneration = ReadProtoDouble(reactorSnapshot, "CurrentElectricalGeneration", double.NaN);
			return !double.IsNaN(currentGeneration) && currentGeneration > 0.01;
		}

		private static double ReadLoadedPowerPercent(PartModule reactorModule)
		{
			double value = ReadModuleDouble(reactorModule, "CurrentPowerPercent", double.NaN);
			if (!double.IsNaN(value) && value >= 0.0)
			{
				return value;
			}

			value = ReadModuleDouble(reactorModule, "CurrentThrottle", double.NaN);
			if (!double.IsNaN(value) && value >= 0.0)
			{
				return value;
			}

			value = ReadModuleDouble(reactorModule, "CurrentReactorThrottle", double.NaN);
			if (!double.IsNaN(value) && value >= 0.0)
			{
				return value;
			}

			return double.NaN;
		}

		private static double ReadProtoPowerPercent(ProtoPartModuleSnapshot reactorSnapshot)
		{
			double value = ReadProtoDouble(reactorSnapshot, "CurrentPowerPercent", double.NaN);
			if (!double.IsNaN(value) && value >= 0.0)
			{
				return value;
			}

			value = ReadProtoDouble(reactorSnapshot, "CurrentThrottle", double.NaN);
			if (!double.IsNaN(value) && value >= 0.0)
			{
				return value;
			}

			value = ReadProtoDouble(reactorSnapshot, "CurrentReactorThrottle", double.NaN);
			if (!double.IsNaN(value) && value >= 0.0)
			{
				return value;
			}

			return double.NaN;
		}

		private static double Clamp(double value, double min, double max)
		{
			if (value < min)
			{
				return min;
			}

			if (value > max)
			{
				return max;
			}

			return value;
		}

		private static void ApplyProcessSettings(PartModule processModule, bool running, double capacity)
		{
			WriteModuleBool(processModule, "running", running);
			WriteModuleBool(processModule, "toggle", false);
			WriteModuleInt(processModule, "valve_i", 2);
			WriteModuleDouble(processModule, "capacity", capacity);
		}

		private static void ApplyProtoProcessSettings(ProtoPartModuleSnapshot processSnapshot, bool running, double capacity)
		{
			WriteProtoString(processSnapshot, "running", running ? "True" : "False");
			WriteProtoString(processSnapshot, "toggle", "False");
			WriteProtoString(processSnapshot, "valve_i", "2");
			WriteProtoString(processSnapshot, "capacity", capacity.ToString("G17", CultureInfo.InvariantCulture));
		}

		private static bool ReadModuleBool(PartModule module, string memberName, bool fallback)
		{
			object value = ReadModuleMember(module, memberName);
			if (value == null)
			{
				return fallback;
			}

			if (value is bool)
			{
				return (bool)value;
			}

			bool parsed;
			if (bool.TryParse(value.ToString(), out parsed))
			{
				return parsed;
			}

			return fallback;
		}

		private static double ReadModuleDouble(PartModule module, string memberName, double fallback)
		{
			object value = ReadModuleMember(module, memberName);
			if (value == null)
			{
				return fallback;
			}

			try
			{
				if (value is float)
				{
					return (float)value;
				}

				if (value is double)
				{
					return (double)value;
				}

				if (value is int)
				{
					return (int)value;
				}

				double parsed;
				if (TryParseDouble(value.ToString(), out parsed))
				{
					return parsed;
				}
			}
			catch
			{
				// no-op
			}

			return fallback;
		}

		private static string ReadModuleString(PartModule module, string memberName)
		{
			object value = ReadModuleMember(module, memberName);
			return value != null ? value.ToString() : null;
		}

		private static object ReadModuleMember(PartModule module, string memberName)
		{
			if (module == null)
			{
				return null;
			}

			try
			{
				BaseField field = module.Fields[memberName];
				if (field != null)
				{
					return field.GetValue(module);
				}
			}
			catch
			{
				// no-op
			}

			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

			try
			{
				FieldInfo fi = module.GetType().GetField(memberName, flags);
				if (fi != null)
				{
					return fi.GetValue(module);
				}
			}
			catch
			{
				// no-op
			}

			try
			{
				PropertyInfo pi = module.GetType().GetProperty(memberName, flags);
				if (pi != null && pi.CanRead)
				{
					return pi.GetValue(module, null);
				}
			}
			catch
			{
				// no-op
			}

			return null;
		}

		private static void WriteModuleBool(PartModule module, string memberName, bool value)
		{
			WriteModuleMember(module, memberName, value);
		}

		private static void WriteModuleInt(PartModule module, string memberName, int value)
		{
			WriteModuleMember(module, memberName, value);
		}

		private static void WriteModuleDouble(PartModule module, string memberName, double value)
		{
			WriteModuleMember(module, memberName, value);
		}

		private static void WriteModuleMember(PartModule module, string memberName, object rawValue)
		{
			if (module == null)
			{
				return;
			}

			bool wrote = false;

			try
			{
				BaseField field = module.Fields[memberName];
				if (field != null)
				{
					object converted = ConvertValue(rawValue, field.FieldInfo.FieldType);
					field.SetValue(converted, module);
					wrote = true;
				}
			}
			catch
			{
				// no-op
			}

			if (wrote)
			{
				return;
			}

			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

			try
			{
				FieldInfo fi = module.GetType().GetField(memberName, flags);
				if (fi != null)
				{
					object converted = ConvertValue(rawValue, fi.FieldType);
					fi.SetValue(module, converted);
					return;
				}
			}
			catch
			{
				// no-op
			}

			try
			{
				PropertyInfo pi = module.GetType().GetProperty(memberName, flags);
				if (pi != null && pi.CanWrite)
				{
					object converted = ConvertValue(rawValue, pi.PropertyType);
					pi.SetValue(module, converted, null);
				}
			}
			catch
			{
				// no-op
			}
		}

		private static object ConvertValue(object value, Type targetType)
		{
			if (targetType == typeof(bool))
			{
				if (value is bool)
				{
					return value;
				}

				bool parsed;
				if (bool.TryParse(value.ToString(), out parsed))
				{
					return parsed;
				}

				return false;
			}

			if (targetType == typeof(int))
			{
				if (value is int)
				{
					return value;
				}

				int parsed;
				if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
				{
					return parsed;
				}

				return 0;
			}

			if (targetType == typeof(float))
			{
				double parsed;
				if (value is float)
				{
					return value;
				}

				if (TryParseDouble(value.ToString(), out parsed))
				{
					return (float)parsed;
				}

				return 0f;
			}

			if (targetType == typeof(double))
			{
				double parsed;
				if (value is double)
				{
					return value;
				}

				if (TryParseDouble(value.ToString(), out parsed))
				{
					return parsed;
				}

				return 0d;
			}

			if (targetType == typeof(string))
			{
				return value.ToString();
			}

			return value;
		}

		private static bool ReadProtoBool(ProtoPartModuleSnapshot module, string key, bool fallback)
		{
			string value = ReadProtoString(module, key);
			if (string.IsNullOrEmpty(value))
			{
				value = ReadProtoString(module, "enabled");
			}

			if (string.IsNullOrEmpty(value))
			{
				return fallback;
			}

			bool parsed;
			if (bool.TryParse(value, out parsed))
			{
				return parsed;
			}

			return fallback;
		}

		private static double ReadProtoDouble(ProtoPartModuleSnapshot module, string key, double fallback)
		{
			string value = ReadProtoString(module, key);
			if (string.IsNullOrEmpty(value))
			{
				return fallback;
			}

			double parsed;
			if (TryParseDouble(value, out parsed))
			{
				return parsed;
			}

			return fallback;
		}

		private static string ReadProtoString(ProtoPartModuleSnapshot module, string key)
		{
			if (module == null || module.moduleValues == null)
			{
				return null;
			}

			try
			{
				return module.moduleValues.GetValue(key);
			}
			catch
			{
				return null;
			}
		}

		private static void WriteProtoString(ProtoPartModuleSnapshot module, string key, string value)
		{
			if (module == null || module.moduleValues == null)
			{
				return;
			}

			try
			{
				if (module.moduleValues.HasValue(key))
				{
					module.moduleValues.SetValue(key, value);
				}
				else
				{
					module.moduleValues.AddValue(key, value);
				}
			}
			catch
			{
				// no-op
			}
		}

		private static bool TryParseDouble(string text, out double value)
		{
			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
			{
				return true;
			}

			if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
			{
				return true;
			}

			value = 0.0;
			return false;
		}
	}
}
