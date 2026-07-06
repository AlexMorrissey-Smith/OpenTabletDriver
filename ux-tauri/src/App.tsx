import { useEffect } from "react";
import { useStore } from "@/lib/store";
import { AppShell } from "@/components/AppShell";
import { SecondaryDialogs } from "@/components/SecondaryDialogs";
import { OverlayHud, parseOverlayHash } from "@/components/OverlayHud";
import { Toaster } from "@/components/ui/sonner";

// The transparent HUD window loads the same bundle with an #overlay= hash;
// render only the HUD there (no store init, no daemon connection).
const overlayRequest = parseOverlayHash(window.location.hash);
if (overlayRequest) document.documentElement.classList.add("overlay-mode");

function App() {
  const init = useStore((s) => s.init);
  useEffect(() => {
    if (!overlayRequest) init();
  }, [init]);

  if (overlayRequest) return <OverlayHud request={overlayRequest} />;

  return (
    <>
      <AppShell />
      <SecondaryDialogs />
      <Toaster />
    </>
  );
}

export default App;
