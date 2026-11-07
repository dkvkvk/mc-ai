import OpenAIApi from 'openai';
import { getKey, hasKey } from '../utils/keys.js';
import { strictFormat } from '../utils/text.js';

export class DeepSeek {
    static prefix = 'deepseek';
    constructor(model_name, url, params) {
        this.model_name = model_name;
        this.params = params;

        let config = {};

        config.baseURL = url || 'https://api.deepseek.com';
        config.apiKey = getKey('DEEPSEEK_API_KEY');

        this.openai = new OpenAIApi(config);
    }

    async sendRequest(turns, systemMessage, stop_seq='***') {
        let messages = [{'role': 'system', 'content': systemMessage}].concat(turns);

        messages = strictFormat(messages);

        const pack = {
            model: this.model_name || "deepseek-chat",
            messages,
            stop: stop_seq,
            ...(this.params || {})
        };

        // 网关（api.cline.bot）偶发 ECONNRESET：连接在 TLS 握手/传输中被重置。
        // 一次失败整轮就废，机器人只会在公屏喊一句英文的 "My brain disconnected"。
        // 这类错误是瞬时的，退避重试两三次基本都能过；真过不去也说人话。
        const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
        const transient = (e) => {
            const code = e && (e.code || (e.cause && e.cause.code));
            const st = e && (e.status || (e.response && e.response.status));
            if (['ECONNRESET', 'ETIMEDOUT', 'ECONNREFUSED', 'EAI_AGAIN', 'EPIPE', 'UND_ERR_SOCKET'].includes(code)) return true;
            if (/socket hang up|network|fetch failed|terminated|other side closed/i.test(String(e && e.message))) return true;
            return (st >= 500 && st < 600) || st === 429;
        };

        for (let attempt = 1; attempt <= 3; attempt++) {
            try {
                console.log('Awaiting deepseek api response...' + (attempt > 1 ? ` (第 ${attempt} 次尝试)` : ''));
                // console.log('Messages:', messages);
                let completion = await this.openai.chat.completions.create(pack);
                // Cline Pass 网关把标准响应包了一层 { data: {...}, success: true }，
                // 标准 OpenAI 客户端读不到 choices，这里解包。
                if (!completion?.choices && completion?.data?.choices) {
                    completion = completion.data;
                }
                if (completion.choices[0].finish_reason == 'length')
                    throw new Error('Context length exceeded');
                console.log('Received.');
                return completion.choices[0].message.content;
            }
            catch (err) {
                if ((err.message == 'Context length exceeded' || err.code == 'context_length_exceeded') && turns.length > 1) {
                    console.log('Context length exceeded, trying again with shorter context.');
                    return await this.sendRequest(turns.slice(1), systemMessage, stop_seq);
                }
                const code = (err && (err.code || (err.cause && err.cause.code))) || (err && err.message);
                if (attempt < 3 && transient(err)) {
                    const wait = attempt === 1 ? 1200 : 3500;
                    console.log(`模型接口连接被中断（${code}），${wait}ms 后重试（${attempt + 1}/3）`);
                    await sleep(wait);
                    continue;
                }
                console.log(err);
                return `连不上模型接口（${code}），刚那句没发出去，等几秒再说一遍就行。`;
            }
        }
        return '连不上模型接口，重试 3 次都失败了——检查一下网络或稍后再试。';
    }

    async embed(text) {
        throw new Error('Embeddings are not supported by Deepseek.');
    }
}



