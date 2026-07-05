import { useEffect } from "react";
import { useStore } from "@/lib/store";
import { AppShell } from "@/components/AppShell";
import { SecondaryDialogs } from "@/components/SecondaryDialogs";
import { Toaster } from "@/components/ui/sonner";

function App() {
  const init = useStore((s) => s.init);
  useEffect(() => {
    init();
  }, [init]);

  return (
    <>
      <AppShell />
      <SecondaryDialogs />
      <Toaster />
    </>
  );
}

export default App;
