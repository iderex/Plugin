# Pull Request

## Summary
Provide a brief description of what this PR changes and why.

## Related Issues
Link related issues or tickets separated by commas.

- Closes #
- Fixes #
- Related to #

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Refactor
- [ ] Performance improvement
- [ ] API / endpoint change
- [ ] Settings schema change
- [ ] Documentation update
- [ ] Build/CI change
- [ ] Other (describe):

## Area
- [ ] Settings sync / profiles
- [ ] Admin defaults / config page
- [ ] Ratings (MDBList / TMDB)
- [ ] Notifications / Push (FCM / relay)
- [ ] Seerr integration
- [ ] Games / Emulators
- [ ] Custom home rows
- [ ] Web Client (Go to Moonfin-Core repo)
- [ ] Other / shared

## Changes Made
List the key changes included in this PR.

-
-
-

## Client Impact
Does this need matching changes in a client repo (Core, Smart-TV, Roku)?

- [ ] No client changes needed
- [ ] Companion client PR(s) required, linked here:
- [ ] New setting keys added. List each key and confirm it matches the client key exactly, including casing:

## Compatibility
- [ ] Change to the settings profile is additive only, no renamed or removed properties
- [ ] New properties use the same type the client sends (a client bool maps to `bool?`, an int to `int?`)
- [ ] Migration added for any renamed or removed settings
- [ ] Older clients still work, unknown fields are ignored and no keys were removed

## Testing
Describe how this change was tested.

- [ ] Built the plugin and deployed to a Jellyfin server
- [ ] Verified against a live client (which one:)
- [ ] Manual testing completed
- [ ] Not tested (explain why):

### Test Steps
1.
2.
3.

## Screenshots (if applicable)
Include config page screenshots or request/response samples where relevant.

## Checklist
- [ ] Code builds successfully
- [ ] Code follows project style and conventions
- [ ] No unnecessary commented-out code
- [ ] No new warnings introduced
- [ ] Any new setting keys match the client-side keys exactly
