; Shipped analyzer releases

## Release 0.5.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DBP1001 | AOT Safety | Warning | Runtime type discovery is not AOT-safe
DBP1002 | AOT Safety | Warning | Reflective invocation or construction is not AOT-safe
DBP1003 | AOT Safety | Error | Runtime code generation is unsupported in AOT players
DBP1004 | AOT Safety | Warning | String-based Unity dispatch is stripping-unsafe
DBP1005 | AOT Safety | Warning | Runtime expression compilation is not AOT-safe by default
