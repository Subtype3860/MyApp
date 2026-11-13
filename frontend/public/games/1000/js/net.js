/*!
 * «1000» — WebSocket-клиент для онлайн-комнат.
 */
(function (root) {
  'use strict';

  class ThousandNet {
    constructor(handlers) {
      this.handlers = handlers || {};
      this.ws = null;
      this.queue = [];
      this.open = false;
      this.closing = false;
      this.url = `${root.location.protocol === 'https:' ? 'wss' : 'ws'}://${root.location.host}/ws`;
    }

    connect() {
      if (this.ws && (this.open || this.ws.readyState === WebSocket.CONNECTING)) {
        return this.open ? Promise.resolve() : this.connectingPromise;
      }
      this.open = false;
      this.closing = false;
      this.ws = new WebSocket(this.url);
      this.connectingPromise = new Promise((resolve, reject) => {
        let settled = false;
        const timer = setTimeout(() => {
          if (settled) return;
          settled = true;
          try {
            this.ws.close();
          } catch (_) {
            /* ignore */
          }
          reject(new Error('Сервер не отвечает'));
        }, 7000);

        this.ws.addEventListener('open', () => {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          this.open = true;
          for (const message of this.queue.splice(0)) this.rawSend(message);
          if (this.handlers.onOpen) this.handlers.onOpen();
          resolve();
        });

        this.ws.addEventListener('message', (event) => {
          let message;
          try {
            message = JSON.parse(event.data);
          } catch (_) {
            return;
          }
          if (this.handlers.onMessage) this.handlers.onMessage(message);
        });

        this.ws.addEventListener('error', () => {
          if (!settled) {
            settled = true;
            clearTimeout(timer);
            reject(new Error('Не удалось подключиться к серверу'));
          }
        });

        this.ws.addEventListener('close', () => {
          const wasOpen = this.open;
          this.open = false;
          if (settled && !this.closing && wasOpen && this.handlers.onClose) {
            this.handlers.onClose();
          }
        });
      });
      return this.connectingPromise.catch((error) => {
        this.ws = null;
        throw error;
      });
    }

    isOpen() {
      return Boolean(this.ws && this.open && this.ws.readyState === WebSocket.OPEN);
    }

    send(message) {
      if (this.isOpen()) {
        this.rawSend(message);
        return true;
      }
      if (this.ws && this.ws.readyState === WebSocket.CONNECTING) {
        this.queue.push(message);
        return true;
      }
      return false;
    }

    rawSend(message) {
      try {
        this.ws.send(JSON.stringify(message));
      } catch (_) {
        /* ignore */
      }
    }

    close() {
      this.closing = true;
      this.open = false;
      this.queue.length = 0;
      if (this.ws) {
        try {
          this.ws.close();
        } catch (_) {
          /* ignore */
        }
      }
      this.ws = null;
    }
  }

  root.ThousandNet = ThousandNet;
})(window);
