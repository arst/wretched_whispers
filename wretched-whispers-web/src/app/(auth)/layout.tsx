export default function AuthLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-doom-card border border-doom-card/50 p-8">
        {children}
      </div>
    </div>
  );
}
