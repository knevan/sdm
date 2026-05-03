export default defineContentScript({
  matches: ["*://*.google.com/*"],
  runAt: "document_idle",

  main() {
    console.log("Hello content.");
  },
});
