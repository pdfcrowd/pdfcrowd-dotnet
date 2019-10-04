using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle( "pdfcrowd" )]
[assembly: AssemblyDescription( "" )]
[assembly: AssemblyCompany( "pdfcrowd" )]
[assembly: AssemblyProduct( "pdfcrowd" )]
[assembly: AssemblyCopyright( "Copyright © pdfcrowd 2010" )]
[assembly: AssemblyTrademark( "" )]
[assembly: AssemblyCulture( "" )]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible( false )]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid( "0c4b5d47-5030-4673-b449-e9608f50745c" )]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Revision and Build Numbers 
// by using the '*' as shown below:
[assembly: AssemblyVersion( "4.11.0" )]
[assembly: AssemblyFileVersion( "4.11.0" )]

#if DEBUG
#if NET20
[assembly: AssemblyConfiguration(".NET Framework 2.0 Debug")]
#elif NETSTANDARD2_0
[assembly: AssemblyConfiguration(".NET Standard 2.0 Debug")]
#elif NETCOREAPP2_0
[assembly: AssemblyConfiguration(".NET Core 2.0 Debug")]
#else
#error Missing AssemblyConfiguration attribute.
#endif
#else
#if NET20
[assembly: AssemblyConfiguration(".NET Framework 2.0")]
#elif NETSTANDARD2_0
[assembly: AssemblyConfiguration(".NET Standard 2.0")]
#elif NETCOREAPP2_0
[assembly: AssemblyConfiguration(".NET Core 2.0")]
#else
#error Missing AssemblyConfiguration attribute.
#endif
#endif
