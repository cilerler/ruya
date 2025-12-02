# Ruya.Services.CloudStorage.Google.Tests

## Configuration

### Test Mode

Unless the environment variable `TEST_MODE` is set to `Integration`, the tests will default to working against the local emulator (`fake-gcs-server`).

- **Emulator Mode (Default)**: `TEST_MODE` is unset or anything other than `Integration`.
- **Integration Mode (Real API)**: `TEST_MODE` = `Integration`.
