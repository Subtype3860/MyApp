const mode = new URLSearchParams(location.search).get('mode');
await import(mode === 'solo' ? './solo.js' : './online.js');
