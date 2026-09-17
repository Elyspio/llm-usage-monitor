import { getUsage } from '../src/usage.ts';
import { inspect } from 'node:util';
const usage = await getUsage();

console.log(inspect(usage, { depth: null }));
