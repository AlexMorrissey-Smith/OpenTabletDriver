import { AboutDialog } from "./dialogs/AboutDialog";
import { UpdaterDialog } from "./dialogs/UpdaterDialog";
import { GreeterDialog } from "./dialogs/GreeterDialog";
import { DeviceStringReaderDialog } from "./dialogs/DeviceStringReaderDialog";
import { PluginManagerDialog } from "./dialogs/PluginManagerDialog";
import { TabletDebuggerDialog } from "./dialogs/TabletDebuggerDialog";

/** All singleton secondary windows, gated by the shared UI dialog slot. */
export function SecondaryDialogs() {
  return (
    <>
      <AboutDialog />
      <UpdaterDialog />
      <GreeterDialog />
      <DeviceStringReaderDialog />
      <PluginManagerDialog />
      <TabletDebuggerDialog />
    </>
  );
}
