import Link from "next/link";

export function Footer() {
  return (
    <footer className="border-t border-white/10 bg-black/20">
      <div className="page-shell flex flex-col gap-4 py-8 text-sm text-steel sm:flex-row sm:items-center sm:justify-between">
        <p>Grinding Thunder — plan the line before you grind it.</p>
        <div className="flex gap-5">
          <Link href="/tree" className="hover:text-white">Tech tree</Link>
          <Link href="/profile" className="hover:text-white">Profile</Link>
        </div>
      </div>
    </footer>
  );
}
