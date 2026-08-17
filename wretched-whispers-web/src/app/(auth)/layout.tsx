export default function AuthLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <div className="bg-doom-card border-doom-card/50 w-full max-w-md border p-8">
        {children}
      </div>
    </div>
  );
}
