//! BLE の通知を改行までつなげて 1 行にする（要件定義書 6.6）
//! デバイスは JSON 1 行を改行付きで送り、1 回の通知に入らなければ分けて送る。

/// 改行が来ないまま溜まり続けたら捨てる長さ（壊れたデータでメモリを使い続けないため）
const MAX_LINE: usize = 4096;

#[derive(Default)]
pub struct LineBuffer {
    buf: Vec<u8>,
}

impl LineBuffer {
    /// 届いた通知を足し、改行で終わった行を返す（改行は含まない）
    pub fn push(&mut self, data: &[u8]) -> Vec<String> {
        self.buf.extend_from_slice(data);
        let mut lines = Vec::new();
        while let Some(i) = self.buf.iter().position(|&b| b == b'\n') {
            let line: Vec<u8> = self.buf.drain(..=i).collect();
            lines.push(String::from_utf8_lossy(&line[..i]).into_owned());
        }
        if self.buf.len() > MAX_LINE {
            self.buf.clear();
        }
        lines
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn whole_line_in_one_notification() {
        let mut b = LineBuffer::default();
        assert_eq!(b.push(b"{\"event\":\"shake\"}\n"), vec!["{\"event\":\"shake\"}"]);
    }

    #[test]
    fn joins_line_split_across_notifications() {
        let mut b = LineBuffer::default();
        assert!(b.push(b"{\"event\":").is_empty());
        assert!(b.push(b"\"knob\",").is_empty());
        assert_eq!(b.push(b"\"level\":2}\n"), vec!["{\"event\":\"knob\",\"level\":2}"]);
    }

    #[test]
    fn two_lines_and_start_of_next_in_one_notification() {
        let mut b = LineBuffer::default();
        assert_eq!(b.push(b"a\nb\nc"), vec!["a", "b"]);
        assert_eq!(b.push(b"d\n"), vec!["cd"]);
    }

    #[test]
    fn joins_multibyte_char_split_across_notifications() {
        let mut b = LineBuffer::default();
        let s = "塩\n".as_bytes();
        assert!(b.push(&s[..1]).is_empty());
        assert_eq!(b.push(&s[1..]), vec!["塩"]);
    }

    #[test]
    fn drops_overlong_data_without_newline() {
        let mut b = LineBuffer::default();
        assert!(b.push(&[b'x'; MAX_LINE + 1]).is_empty());
        assert_eq!(b.push(b"ok\n"), vec!["ok"]);
    }
}
