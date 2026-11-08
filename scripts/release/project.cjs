// Application adapter; the Shared Library and platform contain no application names.
const fs = require('node:fs');
const path = require('node:path');
const config = JSON.parse(fs.readFileSync(path.join(__dirname, '../../cicd/project.json'), 'utf8'));
for (const key of ['id', 'zipPrefix', 'composeProjectPrefix']) {
  if (!/^[A-Za-z0-9][A-Za-z0-9_-]*$/.test(config[key])) throw new Error(`Invalid project ${key}`);
}
module.exports = config;
