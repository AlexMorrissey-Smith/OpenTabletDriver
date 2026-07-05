import { cn } from "@/lib/utils";

/** A flat settings group: small heading + content, separated by whitespace and a
 *  hairline — not a boxed card. Pages stack these instead of nesting Cards, which
 *  read as generated slop. */
export function Section({
  title,
  description,
  children,
  className,
}: {
  title?: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("border-b border-border/60 pb-6 last:border-0 last:pb-0", className)}>
      {title ? (
        <div className="mb-3 select-none">
          <h2 className="text-sm font-semibold">{title}</h2>
          {description ? (
            <p className="text-xs text-muted-foreground">{description}</p>
          ) : null}
        </div>
      ) : null}
      {children}
    </section>
  );
}
